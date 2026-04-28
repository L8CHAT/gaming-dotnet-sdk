using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.GrpcClient.Connection;
using Feivoo.Gaming.GrpcClient.Handlers;
using Feivoo.Gaming.GrpcClient.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vertex.Messaging;
using Vertex.Transport;
using Vertex.Transport.Grpc;

namespace Feivoo.Gaming.GrpcClient;

/// <summary>
/// Feivoo Gaming .NET 客户端 SDK，v2.0.0 — 基于 Vertex.Messaging +
/// Vertex.Transport.Grpc 的重写。每次 <c>RequestAsync</c> 经 Vertex
/// <see cref="IRpcClient.InvokeAsync{TReq,TResp}"/> 走 channel；平台推送的
/// 期号 / Livekit 事件经 <see cref="IMessageBus.Subscribe{T}"/> 订阅后转
/// 发到本类的 12 个公共事件；平台反向发起的 5 个 RPC（OrderSubmit /
/// OrderSettle / OrderRevoke / wallet queries）通过注册的
/// <see cref="IGamingMessageHandler"/> 响应。
///
/// 公共接口在 v2.0.0 里与 v1.3.0 保持一致，但删除了
/// <c>PushAsync</c> 重载（旧的 <c>MESSAGE_MODE_PUSH</c> 模式在 Vertex 的
/// wire 协议里不存在）。调用方若需 fire-and-forget 语义，可以把
/// <c>RequestAsync</c> 拆成独立 Task 并丢弃结果。
/// </summary>
public sealed class GamingClient : IAsyncDisposable
{
    private const string ChannelName = "feivoo-gaming-message";

    private readonly GamingClientOptions _options;
    private readonly IGamingMessageHandler _handler;
    private readonly ILogger<GamingClient> _logger;

    private ServiceProvider? _serviceProvider;
    private IRpcClient? _rpcClient;
    private readonly List<IDisposable> _subscriptions = new();
    private ConnectionState _state;
    private Exception? _lastError;
    private int _disposed;

    public GamingClient(
        IOptions<GamingClientOptions> options,
        IGamingMessageHandler handler,
        ILogger<GamingClient>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? NullLogger<GamingClient>.Instance;
        _state = ConnectionState.Disconnected;
    }

    public ConnectionState State => _state;

    public Exception? LastError => _lastError;

    public bool IsConnected => _state == ConnectionState.Connected;

    // ===== 平台 → 客户端 单向事件 =====

    public event Func<IssueCountdown, CancellationToken, Task>? IssueCountdownReceived;
    public event Func<IssueOpening, CancellationToken, Task>? IssueOpeningReceived;
    public event Func<IssueStopping, CancellationToken, Task>? IssueStoppingReceived;
    public event Func<IssueDrawing, CancellationToken, Task>? IssueDrawingReceived;
    public event Func<IssueFinished, CancellationToken, Task>? IssueFinishedReceived;
    public event Func<IssueCheckout, CancellationToken, Task>? IssueCheckoutReceived;
    public event Func<IssueTerminated, CancellationToken, Task>? IssueTerminatedReceived;
    public event Func<LivekitLiveChanged, CancellationToken, Task>? LivekitLiveChangedReceived;
    public event Func<LivekitRoomStarted, CancellationToken, Task>? LivekitRoomStartedReceived;
    public event Func<LivekitRoomFinished, CancellationToken, Task>? LivekitRoomFinishedReceived;
    public event Func<LivekitTrackPublished, CancellationToken, Task>? LivekitTrackPublishedReceived;
    public event Func<LivekitTrackUnpublished, CancellationToken, Task>? LivekitTrackUnpublishedReceived;

    // ===== 连接生命周期 =====

    public Task ConnectAsync(CancellationToken cancellationToken = default) => EnsureConnectedAsync(cancellationToken);

    public async Task WaitUntilConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_rpcClient is null)
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        while (!IsConnected)
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(GamingClient));
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask CloseAsync() => DisposeAsync();

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_disposed != 0) throw new ObjectDisposedException(nameof(GamingClient));
        if (_rpcClient is not null) return;

        SetState(ConnectionState.Connecting, error: null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpcTransport(ChannelName, o =>
        {
            o.ServerAddress = new Uri(_options.Address);
            o.ConnectTimeout = _options.DialTimeout;
            o.Reconnect = _options.AutoReconnect
                ? new ReconnectPolicy
                {
                    Enabled = true,
                    InitialBackoff = _options.ReconnectDelay,
                    MaxBackoff = _options.MaxReconnectDelay,
                    Multiplier = 2.0,
                    Jitter = 0.1,
                }
                : ReconnectPolicy.Disabled;
            o.Metadata.Add(new KeyValuePair<string, string>("x-tenant-id", _options.AccessId));
            o.Metadata.Add(new KeyValuePair<string, string>("x-secret-key", _options.SecretKey));
            // Mirror the Go SDK: let the server identify the peer by AccessId
            // so ctx.From.Value on server-side handlers is the tenant id.
            o.Metadata.Add(new KeyValuePair<string, string>("x-vertex-peer-id", _options.AccessId));
        });
        services.AddMessagingChannel(ChannelName, reg =>
        {
            // client → server request/response (19)
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
            reg.RegisterRequest<ChannelMemberPointBalanceQuery, ChannelMemberPointBalanceQueryAck>();
            reg.RegisterRequest<ChannelMemberCashbackBalanceQuery, ChannelMemberCashbackBalanceQueryAck>();
            reg.RegisterRequest<ChannelConfigQuery, ChannelConfigQueryAck>();
            reg.RegisterRequest<WaitingOrderListQuery, WaitingOrderListQueryAck>();
            reg.RegisterRequest<LotteryGameQuery, LotteryGameQueryAck>();
            reg.RegisterRequest<LotteryGameList, LotteryGameListAck>();
            reg.RegisterRequest<GameNodeQuery, GameNodeQueryAck>();
            reg.RegisterRequest<LivekitTokenQuery, LivekitTokenQueryAck>();

            // server → client reverse RPCs (5) — client handles via IGamingMessageHandler
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

        // 5 reverse-RPC handlers delegating to IGamingMessageHandler.
        services.AddSingleton(_handler);
        services.AddRpcHandler<OrderSubmit, OrderSubmitAck, OrderSubmitAdapter>(ChannelName);
        services.AddRpcHandler<OrderSettle, OrderSettleAck, OrderSettleAdapter>(ChannelName);
        services.AddRpcHandler<OrderRevoke, OrderRevokeAck, OrderRevokeAdapter>(ChannelName);
        services.AddRpcHandler<ChannelMemberWalletQuery, ChannelMemberWalletQueryAck, WalletQueryAdapter>(ChannelName);
        services.AddRpcHandler<ChannelMemberAllWalletsQuery, ChannelMemberAllWalletsQueryAck, AllWalletsQueryAdapter>(ChannelName);

        _serviceProvider = services.BuildServiceProvider();

        foreach (var hosted in _serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
        {
            await hosted.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        _rpcClient = _serviceProvider.GetRequiredKeyedService<IRpcClient>(ChannelName);
        var bus = _serviceProvider.GetRequiredKeyedService<IMessageBus>(ChannelName);
        var transport = _serviceProvider.GetRequiredKeyedService<ITransport>(ChannelName);

        transport.PeerConnectionChanged += OnPeerConnectionChanged;

        // Fan out 12 push-event subscriptions into the public events.
        _subscriptions.Add(bus.Subscribe<IssueCountdown>((c, t) => Raise(IssueCountdownReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<IssueOpening>((c, t) => Raise(IssueOpeningReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<IssueStopping>((c, t) => Raise(IssueStoppingReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<IssueDrawing>((c, t) => Raise(IssueDrawingReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<IssueFinished>((c, t) => Raise(IssueFinishedReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<IssueCheckout>((c, t) => Raise(IssueCheckoutReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<IssueTerminated>((c, t) => Raise(IssueTerminatedReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<LivekitLiveChanged>((c, t) => Raise(LivekitLiveChangedReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<LivekitRoomStarted>((c, t) => Raise(LivekitRoomStartedReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<LivekitRoomFinished>((c, t) => Raise(LivekitRoomFinishedReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<LivekitTrackPublished>((c, t) => Raise(LivekitTrackPublishedReceived, c.Payload, t)));
        _subscriptions.Add(bus.Subscribe<LivekitTrackUnpublished>((c, t) => Raise(LivekitTrackUnpublishedReceived, c.Payload, t)));

        SetState(ConnectionState.Connected, error: null);
    }

    private async ValueTask Raise<TEvent>(Func<TEvent, CancellationToken, Task>? handler, TEvent payload, CancellationToken ct)
    {
        if (handler is null) return;
        try
        {
            await handler(payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            _logger.LogWarning(ex, "Event handler for {EventType} threw; suppressing so the receive loop stays alive.", typeof(TEvent).Name);
        }
    }

    // ===== 客户端 → 平台：Request / Response (19) =====

    public Task<ChannelCreateAck> RequestAsync(ChannelCreate c, CancellationToken ct = default) => Invoke<ChannelCreate, ChannelCreateAck>(c, ct);
    public Task<ChannelUpdateAck> RequestAsync(ChannelUpdate c, CancellationToken ct = default) => Invoke<ChannelUpdate, ChannelUpdateAck>(c, ct);
    public Task<ChannelQueryAck> RequestAsync(ChannelQuery c, CancellationToken ct = default) => Invoke<ChannelQuery, ChannelQueryAck>(c, ct);
    public Task<ChannelListAck> RequestAsync(ChannelList c, CancellationToken ct = default) => Invoke<ChannelList, ChannelListAck>(c, ct);
    public Task<ChannelDeleteAck> RequestAsync(ChannelDelete c, CancellationToken ct = default) => Invoke<ChannelDelete, ChannelDeleteAck>(c, ct);
    /// <summary>
    /// 商户上送频道消息（用户打字 → 下注/取消/查询）。两类失败统一抛 <see cref="GamingRemoteException"/>：
    /// 服务端抛 GamingRemoteException 走 gRPC trailer 回到调用方；服务端返回 <see cref="ChannelMessageHandled.Rejection"/> in-band
    /// 业务拒绝时 SDK 也包装成同一个异常类型——调用方只需 try/catch 一次。
    /// </summary>
    public async Task<ChannelMessageHandled> RequestAsync(ChannelMessageSubmit c, CancellationToken ct = default)
    {
        var handled = await Invoke<ChannelMessageSubmit, ChannelMessageHandled>(c, ct).ConfigureAwait(false);
        if (handled.Rejection is { } rej)
        {
            throw new GamingRemoteException(rej.Code, rej.Message ?? string.Empty);
        }
        return handled;
    }
    public Task<ChannelMemberEnterAck> RequestAsync(ChannelMemberEnter c, CancellationToken ct = default) => Invoke<ChannelMemberEnter, ChannelMemberEnterAck>(c, ct);
    public Task<ChannelMemberLeaveAck> RequestAsync(ChannelMemberLeave c, CancellationToken ct = default) => Invoke<ChannelMemberLeave, ChannelMemberLeaveAck>(c, ct);
    public Task<ChannelMemberQueryAck> RequestAsync(ChannelMemberQuery c, CancellationToken ct = default) => Invoke<ChannelMemberQuery, ChannelMemberQueryAck>(c, ct);
    public Task<ChannelMemberLookupAck> RequestAsync(ChannelMemberLookup c, CancellationToken ct = default) => Invoke<ChannelMemberLookup, ChannelMemberLookupAck>(c, ct);
    public Task<ChannelMemberTurnoverQueryAck> RequestAsync(ChannelMemberTurnoverQuery c, CancellationToken ct = default) => Invoke<ChannelMemberTurnoverQuery, ChannelMemberTurnoverQueryAck>(c, ct);
    public Task<ChannelMemberRecentOrderQueryAck> RequestAsync(ChannelMemberRecentOrderQuery c, CancellationToken ct = default) => Invoke<ChannelMemberRecentOrderQuery, ChannelMemberRecentOrderQueryAck>(c, ct);
    public Task<ChannelMemberRebateClaimAck> RequestAsync(ChannelMemberRebateClaim c, CancellationToken ct = default) => Invoke<ChannelMemberRebateClaim, ChannelMemberRebateClaimAck>(c, ct);
    public Task<ChannelMemberAllRebatesClaimAck> RequestAsync(ChannelMemberAllRebatesClaim c, CancellationToken ct = default) => Invoke<ChannelMemberAllRebatesClaim, ChannelMemberAllRebatesClaimAck>(c, ct);
    public Task<WaitingOrderListQueryAck> RequestAsync(WaitingOrderListQuery c, CancellationToken ct = default) => Invoke<WaitingOrderListQuery, WaitingOrderListQueryAck>(c, ct);
    public Task<LotteryGameQueryAck> RequestAsync(LotteryGameQuery c, CancellationToken ct = default) => Invoke<LotteryGameQuery, LotteryGameQueryAck>(c, ct);
    public Task<LotteryGameListAck> RequestAsync(LotteryGameList c, CancellationToken ct = default) => Invoke<LotteryGameList, LotteryGameListAck>(c, ct);
    public Task<GameNodeQueryAck> RequestAsync(GameNodeQuery c, CancellationToken ct = default) => Invoke<GameNodeQuery, GameNodeQueryAck>(c, ct);
    public Task<LivekitTokenQueryAck> RequestAsync(LivekitTokenQuery c, CancellationToken ct = default) => Invoke<LivekitTokenQuery, LivekitTokenQueryAck>(c, ct);

    private async Task<TResp> Invoke<TReq, TResp>(TReq request, CancellationToken ct)
        where TReq : notnull
        where TResp : notnull
    {
        if (_disposed != 0) throw new ObjectDisposedException(nameof(GamingClient));
        if (_rpcClient is null)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
        }
        return await _rpcClient!
            .InvokeAsync<TReq, TResp>(request, target: default, timeout: _options.RequestTimeout, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    // ===== State plumbing =====

    private void OnPeerConnectionChanged(object? sender, PeerConnectionEvent e)
    {
        switch (e.State)
        {
            case PeerConnectionState.Connected:
                SetState(ConnectionState.Connected, error: null);
                break;
            case PeerConnectionState.Disconnected:
                if (_disposed != 0) SetState(ConnectionState.Closed, error: null);
                else SetState(_options.AutoReconnect ? ConnectionState.Reconnecting : ConnectionState.Disconnected, error: null);
                break;
        }
    }

    private void SetState(ConnectionState next, Exception? error)
    {
        if (_state == next && error is null) return;
        _state = next;
        if (error is not null) _lastError = error;
        try
        {
            _options.OnStateChange?.Invoke(next, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnStateChange callback threw for state {State}", next);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        SetState(ConnectionState.Closed, error: null);

        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();

        if (_serviceProvider is not null)
        {
            var transport = _serviceProvider.GetKeyedService<ITransport>(ChannelName);
            if (transport is not null) transport.PeerConnectionChanged -= OnPeerConnectionChanged;

            try
            {
                foreach (var hosted in _serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
                {
                    await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Hosted-service stop during dispose raised");
            }

            await _serviceProvider.DisposeAsync().ConfigureAwait(false);
        }

        _rpcClient = null;
        _serviceProvider = null;
    }

    // ===== Reverse-RPC adapters (IGamingMessageHandler → IRpcHandler<>) =====

    internal sealed class OrderSubmitAdapter : IRpcHandler<OrderSubmit, OrderSubmitAck>
    {
        private readonly IGamingMessageHandler _inner;
        public OrderSubmitAdapter(IGamingMessageHandler inner) => _inner = inner;
        public async ValueTask<OrderSubmitAck> HandleAsync(RpcContext<OrderSubmit> ctx, CancellationToken ct)
            => await _inner.OnOrderSubmitAsync(ctx.Request, ct).ConfigureAwait(false);
    }

    internal sealed class OrderSettleAdapter : IRpcHandler<OrderSettle, OrderSettleAck>
    {
        private readonly IGamingMessageHandler _inner;
        public OrderSettleAdapter(IGamingMessageHandler inner) => _inner = inner;
        public async ValueTask<OrderSettleAck> HandleAsync(RpcContext<OrderSettle> ctx, CancellationToken ct)
            => await _inner.OnOrderSettleAsync(ctx.Request, ct).ConfigureAwait(false);
    }

    internal sealed class OrderRevokeAdapter : IRpcHandler<OrderRevoke, OrderRevokeAck>
    {
        private readonly IGamingMessageHandler _inner;
        public OrderRevokeAdapter(IGamingMessageHandler inner) => _inner = inner;
        public async ValueTask<OrderRevokeAck> HandleAsync(RpcContext<OrderRevoke> ctx, CancellationToken ct)
            => await _inner.OnOrderRevokeAsync(ctx.Request, ct).ConfigureAwait(false);
    }

    internal sealed class WalletQueryAdapter : IRpcHandler<ChannelMemberWalletQuery, ChannelMemberWalletQueryAck>
    {
        private readonly IGamingMessageHandler _inner;
        public WalletQueryAdapter(IGamingMessageHandler inner) => _inner = inner;
        public async ValueTask<ChannelMemberWalletQueryAck> HandleAsync(RpcContext<ChannelMemberWalletQuery> ctx, CancellationToken ct)
            => await _inner.OnChannelMemberWalletQueryAsync(ctx.Request, ct).ConfigureAwait(false);
    }

    internal sealed class AllWalletsQueryAdapter : IRpcHandler<ChannelMemberAllWalletsQuery, ChannelMemberAllWalletsQueryAck>
    {
        private readonly IGamingMessageHandler _inner;
        public AllWalletsQueryAdapter(IGamingMessageHandler inner) => _inner = inner;
        public async ValueTask<ChannelMemberAllWalletsQueryAck> HandleAsync(RpcContext<ChannelMemberAllWalletsQuery> ctx, CancellationToken ct)
            => await _inner.OnChannelMemberAllWalletsQueryAsync(ctx.Request, ct).ConfigureAwait(false);
    }
}
