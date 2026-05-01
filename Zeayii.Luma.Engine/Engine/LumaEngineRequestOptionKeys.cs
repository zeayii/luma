namespace Zeayii.Luma.Engine.Engine;

/// <summary>
///     <b>Luma 引擎请求选项键集合</b>
/// </summary>
internal static class LumaEngineRequestOptionKeys
{
    /// <summary>
    ///     请求内容缓存键（用于重试期间复用缓冲内容）。
    /// </summary>
    internal static readonly HttpRequestOptionsKey<byte[]> CachedRequestContentBytesOptionKey = new("luma.cached-request-content-bytes");
}