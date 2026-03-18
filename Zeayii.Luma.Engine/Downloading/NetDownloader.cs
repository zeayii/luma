using System.Net;
using System.Diagnostics.CodeAnalysis;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Infrastructure.Net.Abstractions.Http;

namespace Zeayii.Luma.Engine.Downloading;

/// <summary>
/// <b>基于 Zeayii.Infrastructure.Net.Http 的默认下载器</b>
/// </summary>
/// <param name="netClient">网络客户端入口。</param>
/// <param name="options">引擎运行配置。</param>
[SuppressMessage("Reliability", "CA2007:Do not directly await a Task", Justification = "await using 释放路径不适用 ConfigureAwait 链式写法。")]
public sealed class NetDownloader<TState>(INetClient netClient, LumaEngineOptions options) : IDownloader<TState>
{
    /// <inheritdoc />
    public async ValueTask<HttpResponseMessage> DownloadAsync(LumaRequest request, LumaContext<TState> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var effectiveRouteKind = request.RouteKind != LumaRouteKind.Auto ? request.RouteKind : context.DefaultRouteKind != LumaRouteKind.Auto ? context.DefaultRouteKind : options.DefaultRouteKind;
        var routeKind = effectiveRouteKind == LumaRouteKind.Proxy ? NetRouteKind.Proxy : NetRouteKind.Direct;
        await using var lease = await netClient.RentAsync(routeKind, cancellationToken).ConfigureAwait(false);
        using var timeoutCancellationTokenSource = CreateTimeoutCancellationTokenSource(request, cancellationToken);
        var effectiveCancellationToken = timeoutCancellationTokenSource?.Token ?? cancellationToken;

        try
        {
            var message = request.HttpRequestMessage;
            return await lease.HttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, effectiveCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or WebException)
        {
            var failedResponse = new HttpResponseMessage((HttpStatusCode)599)
            {
                RequestMessage = request.HttpRequestMessage,
                ReasonPhrase = exception.Message,
                Content = new ByteArrayContent([])
            };
            failedResponse.Headers.TryAddWithoutValidation("X-Luma-Transport-Error", exception.GetType().Name);
            return failedResponse;
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

}


