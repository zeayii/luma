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
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>抓取响应。</returns>
    ValueTask<LumaResponse> DownloadAsync(LumaRequest request, CancellationToken cancellationToken);
}

