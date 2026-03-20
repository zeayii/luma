namespace Zeayii.Luma.Abstractions.CommandLine;

/// <summary>
///     <b>站点运行器契约</b>
///     <para>
///         由框架提供强类型实现，命令层仅通过该契约触发站点抓取运行。
///     </para>
/// </summary>
public interface ILumaSiteRunner
{
    /// <summary>
    ///     运行站点抓取任务。
    /// </summary>
    /// <param name="commandName">命令名称。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task RunAsync(string commandName, string runName, CancellationToken cancellationToken);
}