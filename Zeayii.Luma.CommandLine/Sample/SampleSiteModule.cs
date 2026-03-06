using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.CommandLine;
using Zeayii.Luma.CommandLine.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Zeayii.Luma.CommandLine.Sample;

/// <summary>
/// <b>示例站点模块</b>
/// </summary>
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
        services.AddSingleton<IItemSink, MemoryItemSink>();
        services.AddSingleton<ISpider, SampleSpider>();
    }
}

