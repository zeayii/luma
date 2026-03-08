using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Presentation.Configuration;
using Zeayii.Luma.Presentation.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Zeayii.Luma.Presentation.Window;

/// <summary>
/// <b>窗口呈现管理器</b>
/// <para>
/// 采用单线程事件循环和快照驱动渲染。
/// </para>
/// </summary>
public sealed class PresentationManager(PresentationOptions options, ILogManager logManager, IProgressManager progressManager) : IPresentationManager
{
    /// <summary>
    /// 窗口生命周期取消源。
    /// </summary>
    private CancellationTokenSource? _lifecycleCancellationTokenSource;

    /// <summary>
    /// 左侧节点滚动偏移。
    /// </summary>
    private int _nodeOffset;

    /// <summary>
    /// 右侧日志滚动偏移。
    /// </summary>
    private int _logOffset;

    /// <summary>
    /// 启动标记。
    /// </summary>
    private bool _isStarted;

    /// <summary>
    /// 停止请求标记。
    /// </summary>
    private int _stopRequested;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_isStarted)
        {
            throw new InvalidOperationException("PresentationManager already started.");
        }

        _isStarted = true;
        var localCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lifecycleCancellationTokenSource = localCancellationTokenSource;
        _stopRequested = 0;
        logManager.MarkPresentationStarted();

        try
        {
            var lifecycleToken = localCancellationTokenSource.Token;
            if (Console.IsOutputRedirected || Console.IsErrorRedirected)
            {
                while (!lifecycleToken.IsCancellationRequested)
                {
                    try
                    {
                        logManager.DrainPendingEntries();
                        await Task.Delay(options.RefreshInterval, lifecycleToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                return;
            }

            try
            {
                await AnsiConsole.Live(Render()).AutoClear(false).StartAsync(async context =>
                {
                    while (!lifecycleToken.IsCancellationRequested)
                    {
                        PollKeyboardInput();
                        if (Volatile.Read(ref _stopRequested) != 0)
                        {
                            await StopAsync().ConfigureAwait(false);
                        }

                        context.UpdateTarget(Render());

                        try
                        {
                            await Task.Delay(options.RefreshInterval, lifecycleToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    context.UpdateTarget(Render());
                }).ConfigureAwait(false);
            }
            catch (IOException)
            {
                while (!lifecycleToken.IsCancellationRequested)
                {
                    try
                    {
                        logManager.DrainPendingEntries();
                        await Task.Delay(options.RefreshInterval, lifecycleToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            _lifecycleCancellationTokenSource = null;
            localCancellationTokenSource.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        if (_lifecycleCancellationTokenSource is not null)
        {
            await _lifecycleCancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 渲染整个窗口。
    /// </summary>
    /// <returns>可渲染对象。</returns>
    private Rows Render()
    {
        logManager.DrainPendingEntries();
        var progressSnapshot = progressManager.CreateSnapshot();
        var logSnapshot = logManager.CreateSnapshot();

        var header = new Panel(new Markup(
            $"[bold yellow]{Markup.Escape(options.HeaderBrand)}[/]  " +
            $"[grey]Command:[/] [green]{Markup.Escape(progressSnapshot.CommandName)}[/]  " +
            $"[grey]Run:[/] [green]{Markup.Escape(progressSnapshot.RunName)}[/]  " +
            $"[grey]Stored:[/] [blue]{progressSnapshot.StoredItemCount}[/]  " +
            $"[grey]Active:[/] [blue]{progressSnapshot.ActiveRequestCount}[/]  " +
            $"[grey]Queued:[/] [blue]{progressSnapshot.QueuedRequestCount}[/]  " +
            $"[grey]Elapsed:[/] [blue]{progressSnapshot.Elapsed:hh\\:mm\\:ss}[/]"))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Runtime ")
        };

        var visibleNodes = SelectNodes(progressSnapshot.Nodes);
        var visibleLogs = SelectLogs(logSnapshot.Entries);

        var leftPanel = new Panel(new Rows(visibleNodes.Select(static line => (IRenderable)new Markup(line)).ToArray()))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Nodes "),
            Expand = true
        };

        var rightPanel = new Panel(new Rows(visibleLogs.Select(static line => (IRenderable)new Markup(line)).ToArray()))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Logs "),
            Expand = true
        };

        return new Rows(
            header,
            new Columns([leftPanel, rightPanel]) { Expand = true });
    }

    /// <summary>
    /// 选择可见节点文本。
    /// </summary>
    /// <param name="nodes">节点快照集合。</param>
    /// <returns>可见文本集合。</returns>
    private string[] SelectNodes(IReadOnlyList<NodeSnapshot> nodes)
    {
        if (nodes.Count == 0)
        {
            return ["[grey]No nodes[/]"];
        }

        var start = Math.Min(_nodeOffset, Math.Max(0, nodes.Count - 1));
        return nodes
            .Skip(start)
            .Take(20)
            .Select(static node =>
            {
                var reasonText = string.IsNullOrWhiteSpace(node.Reason) ? string.Empty : $" [darkorange]Reason={Markup.Escape(node.Reason)}[/]";
                return $"{new string(' ', node.Depth * 2)}[{ResolveNodeColor(node.Status)}]{Markup.Escape(node.DisplayText)}[/] [grey](Path={Markup.Escape(node.Path)}, Stored={node.StoredCount}, Exists={node.AlreadyExistsCount}, Q={node.QueuedRequestCount}, A={node.ActiveRequestCount})[/]{reasonText}";
            })
            .ToArray();
    }

    /// <summary>
    /// 选择可见日志文本。
    /// </summary>
    /// <param name="logEntries">日志快照集合。</param>
    /// <returns>可见文本集合。</returns>
    private string[] SelectLogs(IReadOnlyList<LogEntry> logEntries)
    {
        if (logEntries.Count == 0)
        {
            return ["[grey]No logs[/]"];
        }

        var start = Math.Min(_logOffset, Math.Max(0, logEntries.Count - 1));
        return logEntries
            .Skip(start)
            .Take(20)
            .Select(static entry => $"[grey]{entry.Timestamp:HH:mm:ss}[/] [{ResolveLogColor(entry.Level)}]{Markup.Escape(entry.Tag)}[/] {Markup.Escape(entry.Message)}")
            .ToArray();
    }

    /// <summary>
    /// 轮询键盘输入。
    /// </summary>
    private void PollKeyboardInput()
    {
        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        _logOffset = Math.Max(0, _logOffset - 1);
                        break;
                    case ConsoleKey.DownArrow:
                        _logOffset++;
                        break;
                    case ConsoleKey.PageUp:
                        _logOffset = Math.Max(0, _logOffset - 10);
                        break;
                    case ConsoleKey.PageDown:
                        _logOffset += 10;
                        break;
                }

                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _nodeOffset = Math.Max(0, _nodeOffset - 1);
                    break;
                case ConsoleKey.DownArrow:
                    _nodeOffset++;
                    break;
                case ConsoleKey.PageUp:
                    _nodeOffset = Math.Max(0, _nodeOffset - 10);
                    break;
                case ConsoleKey.PageDown:
                    _nodeOffset += 10;
                    break;
                case ConsoleKey.Q:
                    Interlocked.Exchange(ref _stopRequested, 1);
                    break;
            }
        }
    }

    /// <summary>
    /// 将节点状态映射为颜色。
    /// </summary>
    private static string ResolveNodeColor(NodeExecutionStatus status)
    {
        return status switch
        {
            NodeExecutionStatus.Running => "green",
            NodeExecutionStatus.Completed => "blue",
            NodeExecutionStatus.Cancelled => "yellow",
            NodeExecutionStatus.Failed => "red",
            NodeExecutionStatus.Stopping => "orange1",
            _ => "white"
        };
    }

    /// <summary>
    /// 将日志等级映射为颜色。
    /// </summary>
    private static string ResolveLogColor(LogLevelKind level)
    {
        return level switch
        {
            LogLevelKind.Trace => "grey",
            LogLevelKind.Debug => "deepskyblue1",
            LogLevelKind.Information => "white",
            LogLevelKind.Warning => "yellow",
            LogLevelKind.Error => "red",
            LogLevelKind.Critical => "bold red",
            _ => "white"
        };
    }
}
