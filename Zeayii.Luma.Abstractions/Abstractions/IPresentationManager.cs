namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>窗口呈现管理器</b>
/// </summary>
public interface IPresentationManager
{
    /// <summary>
    /// 启动呈现循环。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 请求停止窗口。
    /// </summary>
    /// <returns>异步任务。</returns>
    ValueTask StopAsync();
}

