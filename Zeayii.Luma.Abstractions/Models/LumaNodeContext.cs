namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点上下文</b>
/// <para>
/// 供节点运行过程读取运行期资源和身份信息。
/// </para>
/// </summary>
public sealed class LumaNodeContext
{
    /// <summary>
    /// 初始化节点上下文。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="runName">运行名称。</param>
    /// <param name="commandName">命令名称。</param>
    /// <param name="nodePath">节点路径。</param>
    /// <param name="depth">节点深度。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public LumaNodeContext(Guid runId, string runName, string commandName, string nodePath, int depth, CancellationToken cancellationToken)
    {
        RunId = runId;
        RunName = runName ?? string.Empty;
        CommandName = commandName ?? string.Empty;
        NodePath = nodePath ?? throw new ArgumentNullException(nameof(nodePath));
        Depth = depth;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// 运行标识。
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    /// 运行名称。
    /// </summary>
    public string RunName { get; }

    /// <summary>
    /// 命令名称。
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// 节点路径。
    /// </summary>
    public string NodePath { get; }

    /// <summary>
    /// 节点深度。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// 取消令牌。
    /// </summary>
    public CancellationToken CancellationToken { get; }
}

