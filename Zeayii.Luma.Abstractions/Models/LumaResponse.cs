namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>抓取响应模型</b>
/// <para>
/// 表示下载器返回的原始响应结果。
/// </para>
/// </summary>
/// <param name="request">原始请求。</param>
/// <param name="statusCode">状态码。</param>
/// <param name="finalUrl">最终地址。</param>
/// <param name="headers">响应头。</param>
/// <param name="body">响应体。</param>
/// <param name="fetchedAtUtc">抓取完成时间。</param>
/// <param name="error">错误信息。</param>
public sealed class LumaResponse(LumaRequest request, int statusCode, Uri finalUrl, IReadOnlyDictionary<string, string> headers, ReadOnlyMemory<byte> body, DateTimeOffset fetchedAtUtc, string error)
{
    /// <summary>
    /// 空响应头集合。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 原始请求。
    /// </summary>
    public LumaRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));

    /// <summary>
    /// 状态码。
    /// </summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>
    /// 最终地址。
    /// </summary>
    public Uri FinalUrl { get; } = finalUrl ?? throw new ArgumentNullException(nameof(finalUrl));

    /// <summary>
    /// 响应头。
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; } = headers ?? EmptyHeaders;

    /// <summary>
    /// 响应体。
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; } = body;

    /// <summary>
    /// 抓取完成时间。
    /// </summary>
    public DateTimeOffset FetchedAtUtc { get; } = fetchedAtUtc;

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string Error { get; } = error;

    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool IsSuccess => string.IsNullOrWhiteSpace(Error) && StatusCode is >= 200 and < 300;

    /// <summary>
    /// 将响应体按 UTF-8 解码为文本。
    /// </summary>
    /// <returns>文本内容。</returns>
    public string ReadUtf8Text() => Body.IsEmpty ? string.Empty : System.Text.Encoding.UTF8.GetString(Body.Span);
}
