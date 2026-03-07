namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>请求会话策略</b>
/// <para>
/// 指定节点请求在连接与 Cookie 维度上的会话复用偏好。
/// </para>
/// </summary>
public enum RequestSessionPolicy : byte
{
    /// <summary>
    /// 站点级粘滞复用。
    /// </summary>
    SiteSticky = 0,

    /// <summary>
    /// 每次请求随机选择。
    /// </summary>
    PerRequestRandom = 1,

    /// <summary>
    /// 站点级轮询。
    /// </summary>
    SiteRoundRobin = 2
}
