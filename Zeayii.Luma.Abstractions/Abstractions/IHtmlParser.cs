using AngleSharp.Dom;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
///     <b>HTML 解析器契约</b>
/// </summary>
public interface IHtmlParser
{
    /// <summary>
    ///     将文本解析为 HTML 文档对象。
    /// </summary>
    /// <param name="html">HTML 文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文档对象。</returns>
    ValueTask<IDocument> ParseAsync(string html, CancellationToken cancellationToken);
}