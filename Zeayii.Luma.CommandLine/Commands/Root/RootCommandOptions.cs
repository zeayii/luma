namespace Zeayii.Luma.CommandLine.Commands.Root;

/// <summary>
///     根命令参数集合。
/// </summary>
internal sealed class RootCommandOptions
{
    /// <summary>
    ///     根命令全局参数模块。
    /// </summary>
    public required RootGlobalOptionSet Global { get; init; }

    /// <summary>
    ///     创建根命令参数集合。
    /// </summary>
    /// <returns>参数集合。</returns>
    public static RootCommandOptions Create()
    {
        return new RootCommandOptions
        {
            Global = new RootGlobalOptionSet()
        };
    }
}

/// <summary>
///     根命令全局参数模块。
/// </summary>
internal sealed class RootGlobalOptionSet;
