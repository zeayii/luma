using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Downloading;
using Zeayii.Luma.Engine.Engine;
using Zeayii.Luma.Engine.Html;
using Zeayii.Luma.Engine.Policies;
using Zeayii.Luma.Engine.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zeayii.Luma.Engine.Extensions;

/// <summary>
/// <b>Core 模块 DI 扩展</b>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册爬虫核心模块。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">引擎配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddLumaEngine(this IServiceCollection services, LumaEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IRequestScheduler>(_ => new ChannelRequestScheduler(options.RequestChannelCapacity));
        services.TryAddSingleton<IDownloader, NetDownloader>();
        services.TryAddSingleton<IHtmlParser, AngleSharpHtmlParser>();
        services.TryAddSingleton<INodeStopPolicy, ThresholdNodeStopPolicy>();
        services.TryAddSingleton<LumaEngine>();

        return services;
    }
}

