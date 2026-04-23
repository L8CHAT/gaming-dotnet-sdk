using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.GrpcServer;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vertex.Messaging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration extensions for the Feivoo Gaming gRPC server, layered
/// on Vertex (Vertex.Messaging + Vertex.Transport.Grpc).
/// </summary>
public static class FeivooGamingServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Gaming gRPC server infrastructure: the gRPC transport,
    /// messaging channel, the 19 client→server wallet/channel/lottery
    /// handlers (<typeparamref name="THandler"/>), the authenticator, and
    /// an auth-enforcing gRPC interceptor. Also registers a background
    /// <see cref="GamingSessionTracker"/> that wires transport PeerConnectionChanged
    /// events to <see cref="GamingServerOptions.OnSessionConnected"/> /
    /// <see cref="GamingServerOptions.OnSessionDisconnected"/>.
    /// <para>Call <c>app.MapFeivooGamingServer()</c> in the pipeline to expose the endpoint.</para>
    /// </summary>
    /// <typeparam name="THandler">
    /// Business-logic handler that implements <see cref="IRpcHandler{TReq,TResp}"/>
    /// for all 19 wallet/channel/lottery request pairs.
    /// </typeparam>
    /// <typeparam name="TAuth">Singleton authenticator that validates request credentials.</typeparam>
    public static IServiceCollection AddFeivooGamingServer<THandler, TAuth>(
        this IServiceCollection services)
        where THandler : class,
            IRpcHandler<ChannelCreate, ChannelCreateAck>,
            IRpcHandler<ChannelUpdate, ChannelUpdateAck>,
            IRpcHandler<ChannelQuery, ChannelQueryAck>,
            IRpcHandler<ChannelList, ChannelListAck>,
            IRpcHandler<ChannelDelete, ChannelDeleteAck>,
            IRpcHandler<ChannelMessageSubmit, ChannelMessageHandled>,
            IRpcHandler<ChannelMemberEnter, ChannelMemberEnterAck>,
            IRpcHandler<ChannelMemberLeave, ChannelMemberLeaveAck>,
            IRpcHandler<ChannelMemberQuery, ChannelMemberQueryAck>,
            IRpcHandler<ChannelMemberLookup, ChannelMemberLookupAck>,
            IRpcHandler<ChannelMemberTurnoverQuery, ChannelMemberTurnoverQueryAck>,
            IRpcHandler<ChannelMemberRecentOrderQuery, ChannelMemberRecentOrderQueryAck>,
            IRpcHandler<ChannelMemberRebateClaim, ChannelMemberRebateClaimAck>,
            IRpcHandler<ChannelMemberAllRebatesClaim, ChannelMemberAllRebatesClaimAck>,
            IRpcHandler<WaitingOrderListQuery, WaitingOrderListQueryAck>,
            IRpcHandler<LotteryGameQuery, LotteryGameQueryAck>,
            IRpcHandler<LotteryGameList, LotteryGameListAck>,
            IRpcHandler<GameNodeQuery, GameNodeQueryAck>,
            IRpcHandler<LivekitTokenQuery, LivekitTokenQueryAck>
        where TAuth : class, IGamingServerAuthenticator
    {
        // ── Authentication ────────────────────────────────────────────────
        services.AddSingleton<IGamingServerAuthenticator, TAuth>();
        services.AddSingleton<GamingAuthInterceptor>();
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<GamingAuthInterceptor>();
        });

        // ── Options (host app may override via factory registration) ──────
        services.TryAddSingleton(
            Microsoft.Extensions.Options.Options.Create(new GamingServerOptions()));

        // ── Vertex wiring ─────────────────────────────────────────────────
        services.AddGrpcServerTransport(GamingMessageChannel.Name);
        services.AddMessagingChannel(GamingMessageChannel.Name, reg =>
        {
            // client → server request/response (19 pairs)
            reg.RegisterRequest<ChannelCreate, ChannelCreateAck>();
            reg.RegisterRequest<ChannelUpdate, ChannelUpdateAck>();
            reg.RegisterRequest<ChannelQuery, ChannelQueryAck>();
            reg.RegisterRequest<ChannelList, ChannelListAck>();
            reg.RegisterRequest<ChannelDelete, ChannelDeleteAck>();
            reg.RegisterRequest<ChannelMessageSubmit, ChannelMessageHandled>();
            reg.RegisterRequest<ChannelMemberEnter, ChannelMemberEnterAck>();
            reg.RegisterRequest<ChannelMemberLeave, ChannelMemberLeaveAck>();
            reg.RegisterRequest<ChannelMemberQuery, ChannelMemberQueryAck>();
            reg.RegisterRequest<ChannelMemberLookup, ChannelMemberLookupAck>();
            reg.RegisterRequest<ChannelMemberTurnoverQuery, ChannelMemberTurnoverQueryAck>();
            reg.RegisterRequest<ChannelMemberRecentOrderQuery, ChannelMemberRecentOrderQueryAck>();
            reg.RegisterRequest<ChannelMemberRebateClaim, ChannelMemberRebateClaimAck>();
            reg.RegisterRequest<ChannelMemberAllRebatesClaim, ChannelMemberAllRebatesClaimAck>();
            reg.RegisterRequest<WaitingOrderListQuery, WaitingOrderListQueryAck>();
            reg.RegisterRequest<LotteryGameQuery, LotteryGameQueryAck>();
            reg.RegisterRequest<LotteryGameList, LotteryGameListAck>();
            reg.RegisterRequest<GameNodeQuery, GameNodeQueryAck>();
            reg.RegisterRequest<LivekitTokenQuery, LivekitTokenQueryAck>();

            // server → client request/response (5 pairs) — server is the caller
            reg.RegisterRequest<OrderSubmit, OrderSubmitAck>();
            reg.RegisterRequest<OrderSettle, OrderSettleAck>();
            reg.RegisterRequest<OrderRevoke, OrderRevokeAck>();
            reg.RegisterRequest<ChannelMemberWalletQuery, ChannelMemberWalletQueryAck>();
            reg.RegisterRequest<ChannelMemberAllWalletsQuery, ChannelMemberAllWalletsQueryAck>();

            // server → client unidirectional events (12)
            reg.RegisterEvent<IssueCountdown>();
            reg.RegisterEvent<IssueOpening>();
            reg.RegisterEvent<IssueStopping>();
            reg.RegisterEvent<IssueDrawing>();
            reg.RegisterEvent<IssueFinished>();
            reg.RegisterEvent<IssueCheckout>();
            reg.RegisterEvent<IssueTerminated>();
            reg.RegisterEvent<LivekitLiveChanged>();
            reg.RegisterEvent<LivekitRoomStarted>();
            reg.RegisterEvent<LivekitRoomFinished>();
            reg.RegisterEvent<LivekitTrackPublished>();
            reg.RegisterEvent<LivekitTrackUnpublished>();
        });

        // ── Handler registrations: THandler satisfies 19 IRpcHandler<> ────
        services.AddRpcHandler<ChannelCreate, ChannelCreateAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelUpdate, ChannelUpdateAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelQuery, ChannelQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelList, ChannelListAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelDelete, ChannelDeleteAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMessageSubmit, ChannelMessageHandled, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberEnter, ChannelMemberEnterAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberLeave, ChannelMemberLeaveAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberQuery, ChannelMemberQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberLookup, ChannelMemberLookupAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberTurnoverQuery, ChannelMemberTurnoverQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberRecentOrderQuery, ChannelMemberRecentOrderQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberRebateClaim, ChannelMemberRebateClaimAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<ChannelMemberAllRebatesClaim, ChannelMemberAllRebatesClaimAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<WaitingOrderListQuery, WaitingOrderListQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<LotteryGameQuery, LotteryGameQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<LotteryGameList, LotteryGameListAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<GameNodeQuery, GameNodeQueryAck, THandler>(GamingMessageChannel.Name);
        services.AddRpcHandler<LivekitTokenQuery, LivekitTokenQueryAck, THandler>(GamingMessageChannel.Name);

        // ── Session tracker wires OnSessionConnected/Disconnected ─────────
        services.AddSingleton<GamingSessionTracker>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<GamingSessionTracker>());

        return services;
    }
}
