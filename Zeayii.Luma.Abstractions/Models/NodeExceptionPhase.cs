namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>节点异常阶段</b>
/// <para>
/// 标识节点生命周期中发生异常的具体阶段。
/// </para>
/// </summary>
public enum NodeExceptionPhase
{
    /// <summary>
    /// 构建初始请求阶段。
    /// </summary>
    BuildRequests = 0,

    /// <summary>
    /// 处理普通响应阶段。
    /// </summary>
    HandleResponse = 1,

    /// <summary>
    /// 判断是否进入下载阶段。
    /// </summary>
    ShouldDownload = 2,

    /// <summary>
    /// 构建下载请求阶段。
    /// </summary>
    BuildDownloadRequests = 3,

    /// <summary>
    /// 处理下载响应阶段。
    /// </summary>
    HandleDownloadResponse = 4,

    /// <summary>
    /// 持久化过滤阶段。
    /// </summary>
    ShouldPersist = 5,

    /// <summary>
    /// 持久化回调阶段。
    /// </summary>
    OnPersisted = 6
}
