using Feivoo.Gaming.GrpcServer.Abstractions;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// gRPC interceptor that enforces tenant-id / secret-key authentication on
/// every incoming Gaming bidi stream. Runs once per Connect; downstream
/// handlers then execute under the assumption that the caller is
/// authenticated.
///
/// The SDK side also sets <c>x-vertex-peer-id</c> to the same tenant id,
/// so Vertex's <c>RpcContext.From</c> on handlers equals the caller's
/// AccessId. Session lookup by AccessId is therefore a direct PeerId
/// lookup — no separate header→peer map needed.
/// </summary>
public sealed class GamingAuthInterceptor : Interceptor
{
    private const string TenantIdHeader = "x-tenant-id";
    private const string SecretKeyHeader = "x-secret-key";

    /// <summary>Key used to stash the authenticated <see cref="GamingPrincipal"/> on <see cref="ServerCallContext.UserState"/>.</summary>
    public const string PrincipalKey = "gaming.principal";

    private readonly IGamingServerAuthenticator _authenticator;

    public GamingAuthInterceptor(IGamingServerAuthenticator authenticator)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthenticateAsync(context).ConfigureAwait(false);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await AuthenticateAsync(context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    private async Task AuthenticateAsync(ServerCallContext context)
    {
        string? tenantId = null;
        string? secretKey = null;
        foreach (var entry in context.RequestHeaders)
        {
            if (string.Equals(entry.Key, TenantIdHeader, StringComparison.OrdinalIgnoreCase)) tenantId = entry.Value;
            else if (string.Equals(entry.Key, SecretKeyHeader, StringComparison.OrdinalIgnoreCase)) secretKey = entry.Value;
        }

        if (tenantId is null || secretKey is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing x-tenant-id / x-secret-key headers."));
        }

        var principal = await _authenticator
            .AuthenticateAsync(tenantId, secretKey, context.CancellationToken)
            .ConfigureAwait(false);

        if (principal is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid x-tenant-id / x-secret-key."));
        }

        context.UserState[PrincipalKey] = principal;
    }
}
