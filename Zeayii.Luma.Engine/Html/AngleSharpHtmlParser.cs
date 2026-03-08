using AngleSharp;
using AngleSharp.Dom;
using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Engine.Html;

/// <summary>
/// <b>基于 AngleSharp 的默认 HTML 解析器</b>
/// </summary>
public sealed class AngleSharpHtmlParser : IHtmlParser
{
    /// <summary>
    /// 文档浏览上下文。
    /// </summary>
    private readonly IBrowsingContext _browsingContext = BrowsingContext.New(global::AngleSharp.Configuration.Default);

    /// <inheritdoc />
    public async ValueTask<IDocument> ParseAsync(string html, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = await _browsingContext.OpenAsync(static request => request.Content(string.Empty), cancellationToken).ConfigureAwait(false);
        document.DocumentElement.InnerHtml = html;
        return document;
    }
}

