using Microsoft.AspNetCore.Routing;
using Vertex.Transport.Grpc;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Endpoint routing extensions for the Feivoo Gaming gRPC server.
/// </summary>
public static class FeivooGamingServerEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Vertex bidi gRPC service that carries the Gaming messaging
    /// channel. Requires <c>AddFeivooGamingServer</c> to have been called
    /// during service registration.
    /// </summary>
    public static IEndpointConventionBuilder MapFeivooGamingServer(
        this IEndpointRouteBuilder endpoints)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        return endpoints.MapGrpcService<BidiServiceImpl>();
    }
}
