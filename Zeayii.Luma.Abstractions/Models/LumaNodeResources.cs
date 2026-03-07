using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点资源集合</b>
/// <para>
/// 由框架注入，供节点在解析阶段使用的通用能力对象。
/// </para>
/// </summary>
public sealed class LumaNodeResources
{
    /// <summary>
    /// 初始化节点资源集合。
    /// </summary>
    /// <param name="htmlParser">HTML 解析器。</param>
    public LumaNodeResources(IHtmlParser htmlParser)
    {
        HtmlParser = htmlParser ?? throw new ArgumentNullException(nameof(htmlParser));
    }

    /// <summary>
    /// HTML 解析器。
    /// </summary>
    public IHtmlParser HtmlParser { get; }
}
