namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>抓取路由类型</b>
/// <para>
/// 用于描述一次抓取请求应通过何种网络出口发送。
/// </para>
/// </summary>
public enum LumaRouteKind
{
    /// <summary>
    /// 自动路由（由引擎根据默认策略解析为直连或代理）。
    /// </summary>
    Auto = 0,

    /// <summary>
    /// 直连出口。
    /// </summary>
    Direct = 1,

    /// <summary>
    /// 代理出口。
    /// </summary>
    Proxy = 2
}

