using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.CommandLine;
using Zeayii.Luma.CommandLine.Logging;
using Zeayii.Luma.CommandLine.Options;
using Zeayii.Luma.Engine.Engine;
using Zeayii.Luma.Engine.Extensions;
using Zeayii.Luma.Presentation.Extensions;
using global::Infrastructure.Net.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Execution;

/// <summary>
/// <b>站点命令执行器</b>
/// </summary>
internal static class LumaCommandExecutor
{
    /// <summary>
    /// 执行站点命令。
    /// </summary>
    public static async Task<int> ExecuteAsync<TModule>(
        ApplicationOptions applicationOptions,
        CancellationToken cancellationToken)
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
            Console.Error.WriteLine(fileLoggerProviderResult.WarningMessage);
            logManager.Write(Zeayii.Luma.Abstractions.Models.LogLevelKind.Warning, "Logging", fileLoggerProviderResult.WarningMessage);
        }

        var engine = serviceProvider.GetRequiredService<LumaEngine>();
        var spider = serviceProvider.GetRequiredService<ISpider>();
        var presentation = serviceProvider.GetRequiredService<IPresentationManager>();
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var presentationTask = presentation.RunAsync(linkedCancellationTokenSource.Token);

        try
        {
            await engine.RunAsync(spider, applicationOptions.CommandName, applicationOptions.RunName, linkedCancellationTokenSource.Token).ConfigureAwait(false);
            await presentation.StopAsync().ConfigureAwait(false);
            await presentationTask.ConfigureAwait(false);
            return 0;
        }
        catch
        {
            await presentation.StopAsync().ConfigureAwait(false);
            linkedCancellationTokenSource.Cancel();
            throw;
        }
    }
}

