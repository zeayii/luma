using Spectre.Console;
using Spectre.Console.Rendering;
using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Presentation.Configuration;

namespace Zeayii.Luma.Presentation.Window;

/// <summary>
///     <b>窗口呈现管理器</b>
///     <para>
///         采用单线程事件循环和快照驱动渲染。
///     </para>
/// </summary>
public sealed class PresentationManager(PresentationOptions options, ILogManager logManager, IProgressManager progressManager) : IPresentationManager
{
    /// <summary>
    ///     节点区域固定宽度（字符列）。
    /// </summary>
    private const int NodesPanelFixedWidth = 92;

    /// <summary>
    ///     日志区域最小宽度（字符列）。
    /// </summary>
    private const int LogsPanelMinimumWidth = 40;

    /// <summary>
    ///     节点区域最小宽度（字符列）。
    /// </summary>
    private const int NodesPanelMinimumWidth = 28;

    /// <summary>
    ///     启动标记。
    /// </summary>
    private bool _isStarted;

    /// <summary>
    ///     窗口生命周期取消源。
    /// </summary>
    private CancellationTokenSource? _lifecycleCancellationTokenSource;

    /// <summary>
    ///     右侧日志滚动偏移。
    /// </summary>
    private int _logOffset;

    /// <summary>
    ///     左侧节点滚动偏移。
    /// </summary>
    private int _nodeOffset;

    /// <summary>
    ///     停止请求标记。
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
    ///     渲染整个窗口。
    /// </summary>
    /// <returns>可渲染对象。</returns>
    private IRenderable Render()
    {
        logManager.DrainPendingEntries();
        var progressSnapshot = progressManager.CreateSnapshot();
        var logSnapshot = logManager.CreateSnapshot();

        var headerGrid = new Grid();
        headerGrid.AddColumn();
        headerGrid.AddColumn(new GridColumn().RightAligned());
        headerGrid.AddRow(
            new Markup($"[grey]Command:[/] [green]{Markup.Escape(progressSnapshot.CommandName)}[/]  [grey]Run:[/] [green]{Markup.Escape(progressSnapshot.RunName)}[/]"),
            new Markup(
                $@"[grey]Stored:[/] [blue]{progressSnapshot.StoredItemCount}[/]  [grey]Active:[/] [blue]{progressSnapshot.ActiveRequestCount}[/]  [grey]Queued:[/] [blue]{progressSnapshot.QueuedRequestCount}[/]  [grey]Elapsed:[/] [blue]{progressSnapshot.Elapsed:hh\:mm\:ss}[/]")
        );

        var header = new Panel(headerGrid)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Zeayii Luma "),
            Expand = true
        };

        var visibleNodes = SelectNodes(progressSnapshot.Nodes);
        var visibleLogs = SelectLogs(logSnapshot.Entries);

        var leftPanel = new Panel(new Rows(visibleNodes.Select(static IRenderable (line) => new Markup(line)).ToArray())) { Border = BoxBorder.Rounded, Header = new PanelHeader(" Nodes "), Expand = true };
        var rightPanel = new Panel(new Rows(visibleLogs.Select(static IRenderable (line) => new Markup(line)).ToArray())) { Border = BoxBorder.Rounded, Header = new PanelHeader(" Logs "), Expand = true };

        var terminalWidth = AnsiConsole.Profile.Width;
        var maxNodesWidth = Math.Max(NodesPanelMinimumWidth, terminalWidth - LogsPanelMinimumWidth);
        var effectiveNodesWidth = Math.Min(NodesPanelFixedWidth, maxNodesWidth);

        var layout = new Layout("Root");
        layout.SplitRows(new Layout("Header") { Size = 3 }, new Layout("Body"));
        layout["Body"].SplitColumns(new Layout("Nodes") { Size = effectiveNodesWidth }, new Layout("Logs"));
        layout["Header"].Update(header);
        layout["Nodes"].Update(leftPanel);
        layout["Logs"].Update(rightPanel);
        return layout;
    }

    /// <summary>
    ///     选择可见节点文本。
    /// </summary>
    /// <param name="nodes">节点快照集合。</param>
    /// <returns>可见文本集合。</returns>
    private string[] SelectNodes(IReadOnlyList<NodeSnapshot> nodes)
    {
        if (nodes.Count == 0)
        {
            return ["[grey]No nodes[/]"];
        }

        var visibleLineCount = ResolveBodyVisibleLineCount();
        var maxOffset = Math.Max(0, nodes.Count - visibleLineCount);
        _nodeOffset = Math.Clamp(_nodeOffset, 0, maxOffset);
        var start = Math.Max(0, nodes.Count - visibleLineCount - _nodeOffset);
        return nodes
            .Skip(start)
            .Take(visibleLineCount)
            .Select(static node =>
            {
                var reasonText = string.IsNullOrWhiteSpace(node.Reason) ? string.Empty : $" [darkorange]Reason={Markup.Escape(node.Reason)}[/]";
                return
                    $"{new string(' ', node.Depth * 2)}[{ResolveNodeColor(node.Status)}]{Markup.Escape(node.DisplayText)}[/] [grey](Stored={node.StoredCount}, Exists={node.AlreadyExistsCount}, Queued={node.QueuedRequestCount}, Active={node.ActiveRequestCount})[/]{reasonText}";
            })
            .ToArray();
    }

    /// <summary>
    ///     选择可见日志文本。
    /// </summary>
    /// <param name="logEntries">日志快照集合。</param>
    /// <returns>可见文本集合。</returns>
    private string[] SelectLogs(IReadOnlyList<LogEntry> logEntries)
    {
        if (logEntries.Count == 0)
        {
            return ["[grey]No logs[/]"];
        }

        var visibleLineCount = ResolveBodyVisibleLineCount();
        var maxOffset = Math.Max(0, logEntries.Count - visibleLineCount);
        _logOffset = Math.Clamp(_logOffset, 0, maxOffset);
        var start = Math.Max(0, logEntries.Count - visibleLineCount - _logOffset);
        return logEntries
            .Skip(start)
            .Take(visibleLineCount)
            .Select(static entry => $"[grey]{entry.Timestamp:HH:mm:ss}[/] [{ResolveLogColor(entry.Level)}]{Markup.Escape(entry.Tag)}[/] {Markup.Escape(entry.Message)}")
            .ToArray();
    }

    /// <summary>
    ///     解析主体区域可显示行数。
    /// </summary>
    /// <returns>可显示行数。</returns>
    private static int ResolveBodyVisibleLineCount()
    {
        const int headerRows = 3;
        const int panelBorderRows = 2;
        var terminalHeight = Math.Max(1, AnsiConsole.Profile.Height);
        var bodyRows = Math.Max(1, terminalHeight - headerRows);
        return Math.Max(1, bodyRows - panelBorderRows);
    }

    /// <summary>
    ///     轮询键盘输入。
    /// </summary>
    private void PollKeyboardInput()
    {
        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true);
            if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        {
                            _logOffset++;
                            break;
                        }
                    case ConsoleKey.DownArrow:
                        {
                            _logOffset = Math.Max(0, _logOffset - 1);
                            break;
                        }
                    case ConsoleKey.PageUp:
                        {
                            _logOffset += 10;
                            break;
                        }
                    case ConsoleKey.PageDown:
                        {
                            _logOffset = Math.Max(0, _logOffset - 10);
                            break;
                        }
                }

                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    {
                        _nodeOffset++;
                        break;
                    }
                case ConsoleKey.DownArrow:
                    {
                        _nodeOffset = Math.Max(0, _nodeOffset - 1);
                        break;
                    }
                case ConsoleKey.PageUp:
                    {
                        _nodeOffset += 10;
                        break;
                    }
                case ConsoleKey.PageDown:
                    {
                        _nodeOffset = Math.Max(0, _nodeOffset - 10);
                        break;
                    }
                case ConsoleKey.Q:
                    {
                        Interlocked.Exchange(ref _stopRequested, 1);
                        break;
                    }
            }
        }
    }

    /// <summary>
    ///     将节点状态映射为颜色。
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
    ///     将日志等级映射为颜色。
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