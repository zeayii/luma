using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.CommandLine;
using Zeayii.Luma.CommandLine.Infrastructure;
using Zeayii.Luma.Engine.Extensions;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
///     <b>示例站点模块</b>
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "命令模块需要保持 public，供源码生成器扫描并挂载命令。")]
public sealed class SampleSiteModule : ILumaCommandModule
{
    /// <inheritdoc />
    public static string CommandName => "sample";

    /// <inheritdoc />
    public static string Description => "Run sample crawl command.";

    /// <inheritdoc />
    public static void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IItemSink<SampleState>, MemoryItemSink>();
        services.AddLumaSpider<SampleState, SampleSpider>();
    }
}