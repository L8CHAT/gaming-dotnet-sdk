using Feivoo.Gaming.GrpcClient.Connection;

namespace Feivoo.Gaming.GrpcClient.Options;

/// <summary>
/// <see cref="GamingClient"/> 的配置选项。
/// 通过 <c>AddFeivooGamingClient&lt;THandler&gt;(services, configure)</c> 中的回调注入。
/// </summary>
public sealed class GamingClientOptions
{
    /// <summary>
    /// gRPC 服务地址，例如 <c>https://gaming.example.com</c>。
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 认证用 AccessId，写入 gRPC Metadata <c>x-tenant-id</c>。
    /// </summary>
    public string AccessId { get; set; } = string.Empty;

    /// <summary>
    /// 认证密钥，写入 gRPC Metadata <c>x-secret-key</c>。
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 单次 <c>RequestAsync</c> 调用等待平台响应的超时。默认 10 秒。
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 建立 gRPC 连接（含首次连接和每次重连）的超时。默认 10 秒。
    /// 超时后抛出 <see cref="OperationCanceledException"/>。
    /// </summary>
    public TimeSpan DialTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 首次断线后重连前的等待时间。后续重试按指数退避翻倍，上限 <see cref="MaxReconnectDelay"/>。
    /// 默认 500 毫秒。
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 指数退避的上限。重连等待时间不会超过此值。默认 10 秒。
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否在连接意外断开后自动重连。默认 <c>true</c>。
    /// 设为 <c>false</c> 时，断线后不会触发重连，调用方需自行处理。
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 连接状态变更回调。参数为新状态和导致状态变更的异常（正常切换时为 <c>null</c>）。
    /// </summary>
    public Action<ConnectionState, Exception?>? OnStateChange { get; set; }
}
