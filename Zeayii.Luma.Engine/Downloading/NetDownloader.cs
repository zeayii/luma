using System.Net;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Infrastructure.Net.Abstractions.Http;

namespace Zeayii.Luma.Engine.Downloading;

/// <summary>
/// <b>基于 Zeayii.Infrastructure.Net.Http 的默认下载器</b>
/// </summary>
internal sealed class NetDownloader : IDownloader
{
    /// <summary>
    /// 网络客户端入口。
    /// </summary>
    private readonly INetClient _netClient;

    /// <summary>
    /// 引擎运行配置。
    /// </summary>
    private readonly LumaEngineOptions _options;

    /// <summary>
    /// 初始化下载器。
    /// </summary>
    /// <param name="netClient">网络客户端入口。</param>
    /// <param name="options">引擎运行配置。</param>
    public NetDownloader(INetClient netClient, LumaEngineOptions options)
    {
        _netClient = netClient ?? throw new ArgumentNullException(nameof(netClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<LumaResponse> DownloadAsync(LumaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var routeKind = request.RouteKind == LumaRouteKind.Proxy ? NetRouteKind.Proxy : NetRouteKind.Direct;
        await using var lease = await _netClient.RentAsync(routeKind, cancellationToken).ConfigureAwait(false);
        using var timeoutCancellationTokenSource = CreateTimeoutCancellationTokenSource(request, cancellationToken);
        var effectiveCancellationToken = timeoutCancellationTokenSource?.Token ?? cancellationToken;

        using var message = new HttpRequestMessage(request.Method, request.Url);
        foreach (var pair in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }

        if (!request.Body.IsEmpty)
        {
            message.Content = new ReadOnlyMemoryContent(request.Body);
        }

        try
        {
            using var response = await lease.HttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken).ConfigureAwait(false);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            var body = await ReadBodyAsync(response, _options.MaxResponseBodyBytes, effectiveCancellationToken).ConfigureAwait(false);
            return new LumaResponse(
                request,
                (int)response.StatusCode,
                response.RequestMessage?.RequestUri ?? request.Url,
                headers,
                body,
                DateTimeOffset.UtcNow,
                string.Empty);
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or WebException)
        {
            return new LumaResponse(
                request,
                0,
                request.Url,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReadOnlyMemory<byte>.Empty,
                DateTimeOffset.UtcNow,
                exception.Message);
        }
    }

    /// <summary>
    /// 按请求级超时创建联动取消源。
    /// </summary>
    /// <param name="request">抓取请求。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>取消源；未配置超时时返回 null。</returns>
    private static CancellationTokenSource? CreateTimeoutCancellationTokenSource(LumaRequest request, CancellationToken cancellationToken)
    {
        if (request.Timeout is not { } timeout || timeout <= TimeSpan.Zero)
        {
            return null;
        }

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationTokenSource.CancelAfter(timeout);
        return cancellationTokenSource;
    }

    /// <summary>
    /// 流式读取响应体并按上限截断。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="maxBytes">允许读取的最大字节数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应体字节。</returns>
    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        var boundedMaxBytes = Math.Max(8 * 1024, maxBytes);
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > boundedMaxBytes)
        {
            throw new HttpRequestException($"Response body too large. ContentLength={contentLength}, Max={boundedMaxBytes}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memoryStream = new MemoryStream(capacity: Math.Min(boundedMaxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var readCount = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (readCount <= 0)
            {
                return memoryStream.ToArray();
            }

            if (memoryStream.Length + readCount > boundedMaxBytes)
            {
                throw new HttpRequestException($"Response body too large. Max={boundedMaxBytes}");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, readCount), cancellationToken).ConfigureAwait(false);
        }
    }
}

