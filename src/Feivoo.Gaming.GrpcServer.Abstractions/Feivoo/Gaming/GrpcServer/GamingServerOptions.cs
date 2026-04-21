using Feivoo.Gaming.GrpcServer.Abstractions;

namespace Feivoo.Gaming.GrpcServer;

public sealed class GamingServerOptions
{
    /// <summary>服务端 → 客户端 请求的等待超时。</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>会话注册回调（连接建立、认证通过后触发）。</summary>
    public Action<IGamingSession>? OnSessionConnected { get; init; }

    /// <summary>会话注销回调（连接断开/异常时触发）。</summary>
    public Action<IGamingSession, Exception?>? OnSessionDisconnected { get; init; }
}
