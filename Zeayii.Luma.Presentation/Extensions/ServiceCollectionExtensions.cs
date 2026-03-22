using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Presentation.Configuration;
using Zeayii.Luma.Presentation.Core;
using Zeayii.Luma.Presentation.Logging;
using Zeayii.Luma.Presentation.Window;

namespace Zeayii.Luma.Presentation.Extensions;

/// <summary>
///     <b>Presentation 模块 DI 扩展</b>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     注册 Presentation 模块。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">呈现配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddLumaPresentation(this IServiceCollection services, PresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(options);
        if (options.MinimumLogLevel == LogLevel.None)
        {
            services.TryAddSingleton<ILogManager, NullLogManager>();
        }
        else
        {
            services.TryAddSingleton<ILogManager, LogManager>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, PresentationLoggerProvider>());
        }

        services.TryAddSingleton<IProgressManager, ProgressManager>();
        services.TryAddSingleton<IPresentationManager, PresentationManager>();
        return services;
    }
}