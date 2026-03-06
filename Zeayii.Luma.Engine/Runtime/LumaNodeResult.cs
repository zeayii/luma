namespace Zeayii.Luma.Engine.Runtime;

/// <summary>
/// <b>节点执行结果</b>
/// </summary>
internal sealed class LumaNodeResult
{
    /// <summary>
    /// 初始化时间。
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>
    /// 结束节点。
    /// </summary>
    public void Complete()
    {
        EndedAtUtc ??= DateTimeOffset.UtcNow;
    }
}

