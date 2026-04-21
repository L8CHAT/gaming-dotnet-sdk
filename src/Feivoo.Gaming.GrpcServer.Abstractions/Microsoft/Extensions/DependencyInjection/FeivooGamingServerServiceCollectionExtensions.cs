using Feivoo.Gaming.GrpcServer;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Feivoo Gaming 服务端 DI 注册扩展。</summary>
public static class FeivooGamingServerServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Feivoo Gaming 服务端所需的 DI（gRPC + 业务 handler / 认证器）。
    /// 调用方仍需自行 <c>app.MapFeivooGamingServer()</c> 来挂载 endpoint。
    /// </summary>
    public static IServiceCollection AddFeivooGamingServer<THandler, TAuthenticator>(
        this IServiceCollection services,
        Action<GamingServerOptions>? configure = null)
        where THandler : class, IGamingServerHandler
        where TAuthenticator : class, IGamingServerAuthenticator
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddGrpc();
        services.AddOptions<GamingServerOptions>();
        if (configure is not null) services.Configure(configure);

        services.TryAddScoped<IGamingServerHandler, THandler>();
        services.TryAddSingleton<IGamingServerAuthenticator, TAuthenticator>();

        return services;
    }
}
