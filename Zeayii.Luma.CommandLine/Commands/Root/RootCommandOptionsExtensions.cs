using System.CommandLine;
using Zeayii.Luma.CommandLine.Commands.Root;

namespace Zeayii.Luma.CommandLine.Commands;

/// <summary>
///     根命令参数扩展。
/// </summary>
internal static partial class RootCommandExtensions
{
    /// <summary>
    ///     应用根命令参数。
    /// </summary>
    /// <param name="rootCommand">根命令对象。</param>
    /// <param name="options">根命令参数集合。</param>
    public static void ApplyRootOptions(this RootCommand rootCommand, RootCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(options);
        _ = options;
    }
}
