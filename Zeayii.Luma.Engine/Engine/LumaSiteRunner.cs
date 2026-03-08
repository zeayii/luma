using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.CommandLine;

namespace Zeayii.Luma.Engine.Engine;

/// <summary>
/// <b>站点运行器默认实现</b>
/// <para>
/// 以强类型方式桥接站点蜘蛛与泛型引擎，避免反射调用。
/// </para>
/// </summary>
/// <typeparam name="TState">站点运行状态类型。</typeparam>
public sealed class LumaSiteRunner<TState>(LumaEngine<TState> engine, ISpider<TState> spider) : ILumaSiteRunner
{
    /// <inheritdoc />
    public Task RunAsync(string commandName, string runName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(spider);
        return engine.RunAsync(spider, commandName, runName, cancellationToken);
    }
}
