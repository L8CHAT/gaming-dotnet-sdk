using Feivoo.Gaming.GrpcClient;
using Feivoo.Gaming.GrpcClient.Handlers;
using Feivoo.Gaming.GrpcClient.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Feivoo Gaming 客户端 DI 注册扩展。</summary>
public static class FeivooGamingClientServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Feivoo Gaming 客户端 SDK（<see cref="GamingClient"/>）及其依赖。
    /// <para>
    /// <see cref="GamingClient"/> 以单例形式注册，由容器统一管理其生命周期。
    /// 调用方在应用退出前应通过 DI 获取实例并调用 <see cref="GamingClient.DisposeAsync"/>。
    /// </para>
    /// </summary>
    /// <typeparam name="THandler">实现 <see cref="IGamingMessageHandler"/> 的业务处理类。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">
    /// 必需的配置回调，用于设置 <see cref="GamingClientOptions.Address"/>、
    /// <see cref="GamingClientOptions.AccessId"/>、<see cref="GamingClientOptions.SecretKey"/> 等参数。
    /// </param>
    public static IServiceCollection AddFeivooGamingClient<THandler>(
        this IServiceCollection services,
        Action<GamingClientOptions> configure)
        where THandler : class, IGamingMessageHandler
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<GamingClientOptions>().Configure(configure);
        services.TryAddSingleton<IGamingMessageHandler, THandler>();
        services.TryAddSingleton<GamingClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GamingClientOptions>>().Value;
            var handler = sp.GetRequiredService<IGamingMessageHandler>();
            var logger = sp.GetService<ILogger<GamingClient>>();
            return new GamingClient(options, handler, logger);
        });

        return services;
    }
}
