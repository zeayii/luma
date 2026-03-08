using System.Diagnostics.CodeAnalysis;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>爬虫数据项标记接口</b>
/// <para>
/// 所有用户自定义持久化数据结构都应实现该接口。
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "IItem 是框架显式约束的标记接口，用于统一节点产物类型边界。")]
public interface IItem;