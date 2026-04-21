using Feivoo.Gaming.GrpcServer;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Feivoo Gaming 服务端 endpoint 挂载扩展。</summary>
public static class FeivooGamingServerEndpointRouteBuilderExtensions
{
    /// <summary>挂载 gRPC endpoint。等价于 <c>endpoints.MapGrpcService&lt;MessageServiceImpl&gt;()</c>。</summary>
    public static GrpcServiceEndpointConventionBuilder MapFeivooGamingServer(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        return endpoints.MapGrpcService<MessageServiceImpl>();
    }
}
