using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.CommandLine;
using Zeayii.Luma.Engine.Configuration;
using Zeayii.Luma.Engine.Engine;
using Zeayii.Luma.Engine.Html;

namespace Zeayii.Luma.Engine.Extensions;

/// <summary>
///     <b>Core 模块 DI 扩展</b>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">服务集合。</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     注册爬虫核心模块。
        /// </summary>
        /// <param name="options">引擎配置。</param>
        /// <returns>服务集合。</returns>
        public IServiceCollection AddLumaEngine(LumaEngineOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            services.TryAddSingleton(options);
            services.TryAddSingleton<IHtmlParser, AngleSharpHtmlParser>();
            services.TryAddSingleton(typeof(LumaEngine<>));
            return services;
        }

        /// <summary>
        ///     注册站点蜘蛛与强类型运行器。
        /// </summary>
        /// <typeparam name="TState">站点运行状态类型。</typeparam>
        /// <typeparam name="TSpider">站点蜘蛛实现类型。</typeparam>
        /// <returns>服务集合。</returns>
        public IServiceCollection AddLumaSpider<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSpider>()
            where TSpider : class, ISpider<TState>
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<ISpider<TState>, TSpider>();
            services.TryAddSingleton<ILumaSiteRunner, LumaSiteRunner<TState>>();
            return services;
        }
    }
}