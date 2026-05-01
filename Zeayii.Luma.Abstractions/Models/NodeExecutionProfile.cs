namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
///     <b>节点执行画像</b>
///     <para>
///         聚合节点执行所需的类型级配置，避免引擎分散读取多个配置来源。
///     </para>
/// </summary>
public sealed class NodeExecutionProfile
{
    /// <summary>
    ///     阶段执行选项。
    /// </summary>
    public required NodeStageExecutionOptions StageOptions { get; init; }

    /// <summary>
    ///     执行选项。
    /// </summary>
    public required NodeExecutionOptions ExecutionOptions { get; init; }

    /// <summary>
    ///     请求流控选项。
    /// </summary>
    public required NodeFlowControlOptions FlowControlOptions { get; init; }

    /// <summary>
    ///     默认执行画像。
    /// </summary>
    public static NodeExecutionProfile Default { get; } = new()
    {
        StageOptions = NodeStageExecutionOptions.Default,
        ExecutionOptions = NodeExecutionOptions.Default,
        FlowControlOptions = NodeFlowControlOptions.Disabled
    };
}
