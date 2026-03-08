using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>下载器契约</b>
/// </summary>
public interface IDownloader
{
    /// <summary>
    /// 执行下载。
    /// </summary>
    /// <param name="request">抓取请求。</param>
    /// <param name="context">节点上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>原生 HTTP 响应。</returns>
    ValueTask<HttpResponseMessage> DownloadAsync(LumaRequest request, LumaNodeContext context, CancellationToken cancellationToken);
}

