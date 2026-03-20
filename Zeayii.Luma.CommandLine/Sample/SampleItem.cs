using Zeayii.Luma.Abstractions.Abstractions;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
///     <b>示例数据项</b>
/// </summary>
/// <param name="Url">地址。</param>
/// <param name="Title">标题。</param>
internal sealed record SampleItem(string Url, string Title) : IItem
{
    /// <summary>
    ///     返回展示文本。
    /// </summary>
    /// <returns>展示文本。</returns>
    public override string ToString()
    {
        return $"{Title} ({Url})";
    }
}