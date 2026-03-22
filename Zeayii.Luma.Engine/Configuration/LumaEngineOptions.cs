using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Engine.FlowControl;

namespace Zeayii.Luma.Engine.Configuration;

/// <summary>
///     <b>爬虫引擎配置</b>
/// </summary>
public sealed class LumaEngineOptions
{
    /// <summary>
    ///     默认请求路由类型。
    ///     <para>
    ///         当节点请求使用 <see cref="LumaRouteKind.Auto" /> 时，按该策略解析为直连或代理。
    ///     </para>
    /// </summary>
    public required LumaRouteKind DefaultRouteKind { get; init; }

    /// <summary>
    ///     普通请求工作线程数量。
    /// </summary>
    public required int RequestWorkerCount { get; init; }

    /// <summary>
    ///     下载请求工作线程数量。
    /// </summary>
    public required int DownloadWorkerCount { get; init; }

    /// <summary>
    ///     持久化工作线程数量。
    /// </summary>
    public required int PersistWorkerCount { get; init; }

    /// <summary>
    ///     普通请求通道容量。
    /// </summary>
    public required int RequestChannelCapacity { get; init; }

    /// <summary>
    ///     下载请求通道容量。
    /// </summary>
    public required int DownloadChannelCapacity { get; init; }

    /// <summary>
    ///     持久化通道容量。
    /// </summary>
    public required int PersistChannelCapacity { get; init; }

    /// <summary>
    ///     持久化批量大小。
    /// </summary>
    public required int PersistBatchSize { get; init; }

    /// <summary>
    ///     持久化聚合刷新间隔。
    /// </summary>
    public required TimeSpan PersistFlushInterval { get; init; }

    /// <summary>
    ///     呈现刷新间隔。
    /// </summary>
    public required TimeSpan PresentationRefreshInterval { get; init; }

    /// <summary>
    ///     时间提供器。
    ///     <para>
    ///         用于统一引擎内时间读取，便于测试场景注入可控时间源。
    ///     </para>
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    ///     节点流控策略解析器。
    ///     <para>
    ///         为空时使用框架默认解析逻辑。
    ///     </para>
    /// </summary>
    public Func<string?, INodeRequestFlowControlStrategy>? NodeFlowControlStrategyResolver { get; init; }
}