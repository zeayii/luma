using Microsoft.Extensions.DependencyInjection;

namespace Zeayii.Luma.Abstractions.CommandLine;

/// <summary>
///     <b>站点模块契约</b>
///     <para>
///         每个站点实现项目通过该契约向框架注册站点专属服务。
///     </para>
/// </summary>
public interface ILumaCommandModule
{
    /// <summary>
    ///     子命令名称。
    /// </summary>
    static abstract string CommandName { get; }

    /// <summary>
    ///     子命令描述。
    /// </summary>
    static abstract string Description { get; }

    /// <summary>
    ///     注册站点专属服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    static abstract void ConfigureServices(IServiceCollection services);
}