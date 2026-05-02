namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>Luma 停止异常</b>
///     <para>
///         用于在节点实现中显式触发生命周期停止语义。
///     </para>
/// </summary>
public sealed class LumaStopException : Exception
{
    /// <summary>
    ///     初始化停止异常。
    /// </summary>
    /// <param name="code">停止原因代码。</param>
    /// <param name="message">停止说明消息。</param>
    public LumaStopException(string code, string message) : base(message)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "Unknown" : code;
    }

    /// <summary>
    ///     初始化停止异常。
    /// </summary>
    /// <param name="code">停止原因代码。</param>
    /// <param name="message">停止说明消息。</param>
    /// <param name="innerException">内部异常。</param>
    public LumaStopException(string code, string message, Exception innerException) : base(message, innerException)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "Unknown" : code;
    }

    /// <summary>
    ///     停止原因代码。
    /// </summary>
    public string Code { get; }
}
