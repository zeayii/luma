using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zeayii.Infrastructure.Net.Http.Extensions;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.CommandLine;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.CommandLine.Logging;
using Zeayii.Luma.CommandLine.Options;
using Zeayii.Luma.Engine.Extensions;
using Zeayii.Luma.Presentation.Extensions;

namespace Zeayii.Luma.CommandLine.Execution;

/// <summary>
///     <b>站点命令执行器</b>
/// </summary>
internal static class LumaCommandExecutor
{
    /// <summary>
    ///     执行站点命令。
    /// </summary>
    /// <typeparam name="TModule">命令模块类型。</typeparam>
    /// <param name="applicationOptions">应用配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> ExecuteAsync<TModule>(ApplicationOptions applicationOptions, CancellationToken cancellationToken)
        where TModule : ILumaCommandModule
    {
        ArgumentNullException.ThrowIfNull(applicationOptions);

        var fileLoggerProviderResult = FileLoggerProviderFactory.Create(applicationOptions);
        var services = new ServiceCollection();
        services.AddSingleton(applicationOptions);
        services.AddLumaPresentation(OptionsBuilder.BuildPresentationOptions(applicationOptions));
        services.AddNet(OptionsBuilder.BuildNetOptions(applicationOptions));
        services.AddLumaEngine(OptionsBuilder.BuildEngineOptions(applicationOptions));
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(fileLoggerProviderResult.Provider);
        });

        TModule.ConfigureServices(services);
        using var serviceProvider = services.BuildServiceProvider();
        var logManager = serviceProvider.GetRequiredService<ILogManager>();
        if (!string.IsNullOrWhiteSpace(fileLoggerProviderResult.WarningMessage))
        {
            await Console.Error.WriteLineAsync(fileLoggerProviderResult.WarningMessage).ConfigureAwait(false);
            logManager.Write(LogLevelKind.Warning, "Logging", fileLoggerProviderResult.WarningMessage);
        }

        var siteRunner = ResolveSiteRunner(serviceProvider);
        var presentation = serviceProvider.GetRequiredService<IPresentationManager>();
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var presentationTask = presentation.RunAsync(linkedCancellationTokenSource.Token);

        try
        {
            await siteRunner.RunAsync(applicationOptions.CommandName, applicationOptions.RunName, linkedCancellationTokenSource.Token).ConfigureAwait(false);
            await presentation.StopAsync().ConfigureAwait(false);
            await presentationTask.ConfigureAwait(false);
            return 0;
        }
        catch
        {
            await presentation.StopAsync().ConfigureAwait(false);
            await linkedCancellationTokenSource.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     解析站点模块注册的运行器。
    /// </summary>
    /// <param name="serviceProvider">服务容器。</param>
    /// <returns>站点运行器。</returns>
    private static ILumaSiteRunner ResolveSiteRunner(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var runners = serviceProvider.GetServices<ILumaSiteRunner>().ToArray();
        return runners.Length switch
        {
            1 => runners[0],
            0 => throw new InvalidOperationException("No ILumaSiteRunner registration found."),
            _ => throw new InvalidOperationException("Multiple ILumaSiteRunner registrations found. Exactly one site runner is required.")
        };
    }
}