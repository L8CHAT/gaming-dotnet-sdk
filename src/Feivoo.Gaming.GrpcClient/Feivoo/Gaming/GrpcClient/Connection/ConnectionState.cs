namespace Feivoo.Gaming.GrpcClient.Connection;

/// <summary>
/// <see cref="GamingClient"/> 的连接状态。
/// </summary>
public enum ConnectionState
{
    /// <summary>当前未连接（初始状态或连接断开后）。</summary>
    Disconnected,

    /// <summary>正在建立连接。</summary>
    Connecting,

    /// <summary>已成功建立 gRPC 双向流，可正常收发消息。</summary>
    Connected,

    /// <summary>
    /// 连接断开，正在以指数退避重连（仅当 <c>GamingClientOptions.AutoReconnect</c> 为 <c>true</c> 时出现）。
    /// </summary>
    Reconnecting,

    /// <summary>客户端已调用 <c>DisposeAsync()</c>，连接已永久关闭。</summary>
    Closed,
}
