using Zeayii.Luma.Abstractions.Models;

namespace Zeayii.Luma.Abstractions.Abstractions;

/// <summary>
/// <b>进度管理器</b>
/// <para>
/// 负责记录运行期节点状态，并生成呈现层使用的快照。
/// </para>
/// </summary>
public interface IProgressManager
{
    /// <summary>
    /// 发布运行快照。
    /// </summary>
    /// <param name="snapshot">快照对象。</param>
    void Publish(ProgressSnapshot snapshot);

    /// <summary>
    /// 创建当前快照。
    /// </summary>
    /// <returns>进度快照。</returns>
    ProgressSnapshot CreateSnapshot();
}
