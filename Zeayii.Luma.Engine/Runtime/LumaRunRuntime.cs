namespace Zeayii.Luma.Engine.Runtime;

/// <summary>
///     <b>运行时宿主</b>
/// </summary>
internal sealed class LumaRunRuntime : IAsyncDisposable
{
    /// <summary>
    ///     释放标记。
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     运行状态。
    /// </summary>
    private string _status = "Running";

    /// <summary>
    ///     初始化运行时宿主。
    /// </summary>
    /// <param name="commandName">命令名称。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="parentToken">父级取消令牌。</param>
    /// <param name="timeProvider">时间提供器。</param>
    public LumaRunRuntime(string commandName, string runName, CancellationToken parentToken, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        CommandName = commandName;
        RunName = string.IsNullOrWhiteSpace(runName) ? $"{CommandName}-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}" : runName;
        RunId = Guid.NewGuid();
        CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        StartedAtUtc = timeProvider.GetUtcNow();
    }

    /// <summary>
    ///     运行标识。
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    ///     命令名称。
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    ///     运行名称。
    /// </summary>
    public string RunName { get; }

    /// <summary>
    ///     启动时间。
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>
    ///     运行取消源。
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; }

    /// <summary>
    ///     运行取消令牌。
    /// </summary>
    public CancellationToken Token => CancellationTokenSource.Token;

    /// <summary>
    ///     运行状态。
    /// </summary>
    public string Status => Volatile.Read(ref _status);

    /// <summary>
    ///     释放运行时。
    /// </summary>
    /// <returns>异步任务。</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        CancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     设置运行状态。
    /// </summary>
    /// <param name="status">状态文本。</param>
    public void SetStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        Volatile.Write(ref _status, status);
    }
}