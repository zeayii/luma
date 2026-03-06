namespace Zeayii.Luma.Abstractions.Models;

/// <summary>
/// <b>抓取路由类型</b>
/// <para>
/// 用于描述一次抓取请求应通过何种网络出口发送。
/// </para>
/// </summary>
public enum LumaRouteKind : byte
{
    /// <summary>
    /// 直连出口。
    /// </summary>
    Direct = 0,

    /// <summary>
    /// 代理出口。
    /// </summary>
    Proxy = 1
}

