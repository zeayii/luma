namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>停止作用域</b>
///     <para>
///         描述停止信号应当影响的生命周期边界。
///     </para>
/// </summary>
public enum LumaStopScope
{
    /// <summary>
    ///     仅停止当前节点及其下游。
    /// </summary>
    Node = 0,

    /// <summary>
    ///     停止当前整次运行。
    /// </summary>
    Run = 1,

    /// <summary>
    ///     停止当前应用宿主。
    /// </summary>
    App = 2
}