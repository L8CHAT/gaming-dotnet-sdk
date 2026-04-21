namespace Feivoo.Gaming.GrpcServer.Abstractions;

/// <summary>
/// 平台对客户端的认证：校验 gRPC metadata 中的 <c>x-tenant-id</c> 与 <c>x-secret-key</c>。
/// 通过则返回认证主体（含 AccessId 与可选自定义状态）；不通过返回 <c>null</c>，连接将被拒绝。
/// </summary>
public interface IGamingServerAuthenticator
{
    Task<GamingPrincipal?> AuthenticateAsync(
        string accessId,
        string secretKey,
        CancellationToken cancellationToken = default);
}
