using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.Grpc.Messaging;
using Feivoo.Gaming.GrpcClient.Connection;
using Feivoo.Gaming.GrpcClient.Handlers;
using Feivoo.Gaming.GrpcClient.Options;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Feivoo.Gaming.GrpcClient;

/// <summary>
/// Feivoo Gaming SDK 客户端（扁平 envelope 架构）。
/// <para>
/// 公开面：
/// <list type="bullet">
///   <item>客户端 → 平台：仅提供 <c>RequestAsync</c>/<c>PushAsync</c> 重载，参数限定为各具体消息类型。</item>
///   <item>平台 → 客户端：必须处理的业务通过 <see cref="IGamingMessageHandler"/> 强制实现；
///     仅信息性的推送（期号事件、直播事件）通过相应 <c>*Received</c> 事件暴露。</item>
/// </list>
/// 原始 <c>ClientMessage</c>/<c>ServerMessage</c> envelope 与 <c>*Ack</c> 的回写完全由 SDK 内部完成，
/// 调用方无法构造、无法发送，避免误用。
/// </para>
/// </summary>
public sealed class GamingClient : IAsyncDisposable
{
    private const string TenantHeader = "x-tenant-id";
    private const string SecretHeader = "x-secret-key";

    private readonly GamingClientOptions _options;
    private readonly IGamingMessageHandler _handler;
    private readonly ILogger<GamingClient> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly CancellationTokenSource _lifecycleCts = new();
    private readonly RequestResponseCoordinator<ServerMessage> _pending = new();

    private GrpcChannel? _channel;
    private MessageService.MessageServiceClient? _client;
    private AsyncDuplexStreamingCall<ClientMessage, ServerMessage>? _call;
    private CancellationTokenSource? _streamCts;
    private Task? _readLoop;
    private Task? _reconnectTask;
    private TaskCompletionSource<bool> _connectedTcs = CreateSignal();
    private ConnectionState _state = ConnectionState.Disconnected;
    private Exception? _lastError;
    private bool _disposed;

    /// <summary>
    /// 构造客户端。<paramref name="handler"/> 为必需参数：平台会下发要求客户端必须响应的业务请求，
    /// 不传将直接抛出 <see cref="ArgumentNullException"/>。
    /// </summary>
    public GamingClient(
        GamingClientOptions options,
        IGamingMessageHandler handler,
        ILogger<GamingClient>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handler = handler ?? throw new ArgumentNullException(
            nameof(handler),
            "IGamingMessageHandler 是必需的：平台会下发要求客户端必须响应的业务请求。");
        _logger = logger ?? NullLogger<GamingClient>.Instance;
    }

    /// <summary>当前连接状态。</summary>
    public ConnectionState State => _state;

    /// <summary>最近一次导致断线的异常，无异常时为 <c>null</c>。</summary>
    public Exception? LastError => _lastError;

    /// <summary>是否已建立连接并处于 <see cref="ConnectionState.Connected"/> 状态。</summary>
    public bool IsConnected => _call is not null && _state == ConnectionState.Connected;

    // ===== 仅信息性推送事件：期号生命周期 =====

    public event Func<IssueCountdown, CancellationToken, Task>? IssueCountdownReceived;
    public event Func<IssueOpening, CancellationToken, Task>? IssueOpeningReceived;
    public event Func<IssueStopping, CancellationToken, Task>? IssueStoppingReceived;
    public event Func<IssueDrawing, CancellationToken, Task>? IssueDrawingReceived;
    public event Func<IssueFinished, CancellationToken, Task>? IssueFinishedReceived;
    public event Func<IssueCheckout, CancellationToken, Task>? IssueCheckoutReceived;
    public event Func<IssueTerminated, CancellationToken, Task>? IssueTerminatedReceived;

    // ===== 仅信息性推送事件：直播 =====

    public event Func<LivekitLiveChanged, CancellationToken, Task>? LivekitLiveChangedReceived;
    public event Func<LivekitRoomStarted, CancellationToken, Task>? LivekitRoomStartedReceived;
    public event Func<LivekitRoomFinished, CancellationToken, Task>? LivekitRoomFinishedReceived;
    public event Func<LivekitTrackPublished, CancellationToken, Task>? LivekitTrackPublishedReceived;
    public event Func<LivekitTrackUnpublished, CancellationToken, Task>? LivekitTrackUnpublishedReceived;

    /// <summary>
    /// 主动发起连接。若已连接则立即返回。
    /// 首次 <c>RequestAsync</c>/<c>PushAsync</c> 也会自动触发连接。
    /// 连接超时由 <see cref="GamingClientOptions.DialTimeout"/> 控制。
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
        => EnsureConnectedAsync(cancellationToken);

    /// <summary>等待直到连接建立。若已连接则立即返回。</summary>
    public Task WaitUntilConnectedAsync(CancellationToken cancellationToken = default)
        => IsConnected ? Task.CompletedTask : _connectedTcs.Task.WaitAsync(cancellationToken);

    // ===== 客户端 → 平台：Request/Response =====

    // Channel
    public Task<ChannelCreateAck> RequestAsync(ChannelCreate c, CancellationToken ct = default)
        => Req(E(m => m.ChannelCreate = c), r => r.ChannelCreate, ct);
    public Task<ChannelUpdateAck> RequestAsync(ChannelUpdate c, CancellationToken ct = default)
        => Req(E(m => m.ChannelUpdate = c), r => r.ChannelUpdate, ct);
    public Task<ChannelQueryAck> RequestAsync(ChannelQuery c, CancellationToken ct = default)
        => Req(E(m => m.ChannelQuery = c), r => r.ChannelQuery, ct);
    public Task<ChannelListAck> RequestAsync(ChannelList c, CancellationToken ct = default)
        => Req(E(m => m.ChannelList = c), r => r.ChannelList, ct);
    public Task<ChannelDeleteAck> RequestAsync(ChannelDelete c, CancellationToken ct = default)
        => Req(E(m => m.ChannelDelete = c), r => r.ChannelDelete, ct);

    // ChannelMessage
    public Task<ChannelMessageSubmitAck> RequestAsync(ChannelMessageSubmit c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMessageSubmit = c), r => r.ChannelMessageSubmit, ct);

    // ChannelMember
    public Task<ChannelMemberEnterAck> RequestAsync(ChannelMemberEnter c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberEnter = c), r => r.ChannelMemberEnter, ct);
    public Task<ChannelMemberLeaveAck> RequestAsync(ChannelMemberLeave c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberLeave = c), r => r.ChannelMemberLeave, ct);
    public Task<ChannelMemberQueryAck> RequestAsync(ChannelMemberQuery c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberQuery = c), r => r.ChannelMemberQuery, ct);
    public Task<ChannelMemberLookupAck> RequestAsync(ChannelMemberLookup c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberLookup = c), r => r.ChannelMemberLookup, ct);
    public Task<ChannelMemberTurnoverQueryAck> RequestAsync(ChannelMemberTurnoverQuery c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberTurnoverQuery = c), r => r.ChannelMemberTurnoverQuery, ct);
    public Task<ChannelMemberRecentOrderQueryAck> RequestAsync(ChannelMemberRecentOrderQuery c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberRecentOrderQuery = c), r => r.ChannelMemberRecentOrderQuery, ct);
    public Task<ChannelMemberRebateClaimAck> RequestAsync(ChannelMemberRebateClaim c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberRebateClaim = c), r => r.ChannelMemberRebateClaim, ct);
    public Task<ChannelMemberAllRebatesClaimAck> RequestAsync(ChannelMemberAllRebatesClaim c, CancellationToken ct = default)
        => Req(E(m => m.ChannelMemberAllRebatesClaim = c), r => r.ChannelMemberAllRebatesClaim, ct);

    // WaitingOrderListQuery
    public Task<WaitingOrderListQueryAck> RequestAsync(WaitingOrderListQuery c, CancellationToken ct = default)
        => Req(E(m => m.WaitingOrderListQuery = c), r => r.WaitingOrderListQuery, ct);

    // LotteryGame
    public Task<LotteryGameQueryAck> RequestAsync(LotteryGameQuery c, CancellationToken ct = default)
        => Req(E(m => m.LotteryGameQuery = c), r => r.LotteryGameQuery, ct);
    public Task<LotteryGameListAck> RequestAsync(LotteryGameList c, CancellationToken ct = default)
        => Req(E(m => m.LotteryGameList = c), r => r.LotteryGameList, ct);

    // GameNode
    public Task<GameNodeQueryAck> RequestAsync(GameNodeQuery c, CancellationToken ct = default)
        => Req(E(m => m.GameNodeQuery = c), r => r.GameNodeQuery, ct);

    // Livekit
    public Task<LivekitTokenQueryAck> RequestAsync(LivekitTokenQuery c, CancellationToken ct = default)
        => Req(E(m => m.LivekitTokenQuery = c), r => r.LivekitTokenQuery, ct);

    // ===== 客户端 → 平台：Push（单向，不等待响应）=====

    public Task PushAsync(ChannelCreate c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelCreate = c), ct);
    public Task PushAsync(ChannelUpdate c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelUpdate = c), ct);
    public Task PushAsync(ChannelQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelQuery = c), ct);
    public Task PushAsync(ChannelList c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelList = c), ct);
    public Task PushAsync(ChannelDelete c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelDelete = c), ct);
    public Task PushAsync(ChannelMessageSubmit c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMessageSubmit = c), ct);
    public Task PushAsync(ChannelMemberEnter c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberEnter = c), ct);
    public Task PushAsync(ChannelMemberLeave c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberLeave = c), ct);
    public Task PushAsync(ChannelMemberQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberQuery = c), ct);
    public Task PushAsync(ChannelMemberLookup c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberLookup = c), ct);
    public Task PushAsync(ChannelMemberTurnoverQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberTurnoverQuery = c), ct);
    public Task PushAsync(ChannelMemberRecentOrderQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberRecentOrderQuery = c), ct);
    public Task PushAsync(ChannelMemberRebateClaim c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberRebateClaim = c), ct);
    public Task PushAsync(ChannelMemberAllRebatesClaim c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.ChannelMemberAllRebatesClaim = c), ct);
    public Task PushAsync(WaitingOrderListQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.WaitingOrderListQuery = c), ct);
    public Task PushAsync(LotteryGameQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.LotteryGameQuery = c), ct);
    public Task PushAsync(LotteryGameList c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.LotteryGameList = c), ct);
    public Task PushAsync(GameNodeQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.GameNodeQuery = c), ct);
    public Task PushAsync(LivekitTokenQuery c, CancellationToken ct = default)
        => SendEnvelopeAsync(P(m => m.LivekitTokenQuery = c), ct);

    /// <summary>关闭连接并释放资源。等价于 <see cref="DisposeAsync"/>。</summary>
    public ValueTask CloseAsync() => DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        TransitionTo(ConnectionState.Closed, null);

        _lifecycleCts.Cancel();
        _streamCts?.Cancel();

        try
        {
            if (_call is not null) await _call.RequestStream.CompleteAsync().ConfigureAwait(false);
        }
        catch { /* ignore */ }

        try
        {
            if (_readLoop is not null) await _readLoop.ConfigureAwait(false);
        }
        catch { /* ignore */ }

        try
        {
            if (_reconnectTask is not null) await _reconnectTask.ConfigureAwait(false);
        }
        catch { /* ignore */ }

        _call?.Dispose();
        if (_channel is not null) await _channel.ShutdownAsync().ConfigureAwait(false);

        _pending.FailAll(new ObjectDisposedException(nameof(GamingClient)));
        _writeLock.Dispose();
        _connectLock.Dispose();
        _streamCts?.Dispose();
        _lifecycleCts.Dispose();
    }

    // ===== 内部：信封构建（Request / Push）=====

    private static ClientMessage E(Action<ClientMessage> set)
    {
        var m = new ClientMessage { MessageId = MessageIdGenerator.Ensure(null), Mode = MessageMode.Request };
        set(m);
        return m;
    }

    private static ClientMessage P(Action<ClientMessage> set)
    {
        var m = new ClientMessage { MessageId = MessageIdGenerator.Ensure(null), Mode = MessageMode.Push };
        set(m);
        return m;
    }

    // ===== 内部：核心 Request/Push 助手 =====

    private async Task<TAck> Req<TAck>(
        ClientMessage envelope,
        Func<ServerMessage, TAck?> pick,
        CancellationToken ct)
    {
        var reply = await RequestEnvelopeAsync(envelope, ct).ConfigureAwait(false);
        if (reply.Failure is { } f)
            throw new InvalidOperationException($"[{f.Code}] {f.Message}");
        return pick(reply)
               ?? throw new InvalidOperationException(
                   $"Server reply did not contain expected payload, got '{reply.PayloadCase}'.");
    }

    private Task<ServerMessage> RequestEnvelopeAsync(ClientMessage envelope, CancellationToken cancellationToken)
    {
        envelope.Mode = MessageMode.Request;
        return _pending.SendAndWaitAsync(
            envelope.MessageId,
            async () =>
            {
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
            },
            _options.RequestTimeout,
            cancellationToken);
    }

    private async Task SendEnvelopeAsync(ClientMessage envelope, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    // ===== 内部：连接管理 =====

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (IsConnected) return;

        using var dialCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifecycleCts.Token);
        dialCts.CancelAfter(_options.DialTimeout);

        await ConnectCoreAsync(dialCts.Token).ConfigureAwait(false);
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected) return;

            TransitionTo(ConnectionState.Connecting);

            _channel ??= GrpcChannel.ForAddress(_options.Address);
            _client ??= new MessageService.MessageServiceClient(_channel);

            var headers = new Metadata
            {
                { TenantHeader, _options.AccessId ?? string.Empty },
                { SecretHeader, _options.SecretKey ?? string.Empty },
            };

            _streamCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            _call = _client.Connect(headers, cancellationToken: _streamCts.Token);
            _readLoop = Task.Run(() => ReadLoopAsync(_streamCts.Token));

            _connectedTcs.TrySetResult(true);
            TransitionTo(ConnectionState.Connected);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            TransitionTo(ConnectionState.Disconnected, ex);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void CleanupCurrentCall()
    {
        var call = Interlocked.Exchange(ref _call, null);
        call?.Dispose();
        var cts = Interlocked.Exchange(ref _streamCts, null);
        cts?.Dispose();
    }

    private async Task WriteAsync(ClientMessage message, CancellationToken cancellationToken)
    {
        var call = _call ?? throw new InvalidOperationException("Client is not connected.");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await call.RequestStream.WriteAsync(message).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var intentionalStop = false;
        try
        {
            var call = _call!;
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                var message = call.ResponseStream.Current;
                message.MessageId = MessageIdGenerator.Ensure(message.MessageId);

                if (_pending.TryComplete(message.MessageId, message)) continue;

                _ = DispatchAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (_lifecycleCts.IsCancellationRequested || _disposed)
        {
            intentionalStop = true;
        }
        catch (OperationCanceledException)
        {
            // streamCts cancelled but not lifecycle — treat as disconnect
        }
        catch (Exception ex)
        {
            _lastError = ex;
            _pending.FailAll(ex);
            _logger.LogError(ex, "Receive loop terminated unexpectedly.");
        }

        CleanupCurrentCall();

        if (intentionalStop || _disposed) return;

        TransitionTo(ConnectionState.Disconnected, _lastError);

        if (_options.AutoReconnect)
        {
            _reconnectTask = Task.Run(() => ReconnectWithBackoffAsync());
        }
    }

    private async Task ReconnectWithBackoffAsync()
    {
        var delay = _options.ReconnectDelay;
        var attempt = 0;

        while (!_disposed && !_lifecycleCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, _lifecycleCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_disposed || _lifecycleCts.IsCancellationRequested) return;

            attempt++;
            _logger.LogInformation("Reconnect attempt #{Attempt} after {DelayMs}ms.", attempt, (int)delay.TotalMilliseconds);

            try
            {
                await ConnectCoreAsync(_lifecycleCts.Token).ConfigureAwait(false);
                _logger.LogInformation("Reconnected successfully on attempt #{Attempt}.", attempt);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _lastError = ex;
                _logger.LogWarning(ex, "Reconnect attempt #{Attempt} failed.", attempt);
                delay = delay.TotalMilliseconds * 2 >= _options.MaxReconnectDelay.TotalMilliseconds
                    ? _options.MaxReconnectDelay
                    : TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }
    }

    // ===== 内部：消息分发 =====

    private async Task DispatchAsync(ServerMessage message, CancellationToken ct)
    {
        switch (message.PayloadCase)
        {
            // ===== 平台 → 客户端：必须处理的回调（订单生命周期 + 钱包查询）=====

            case ServerMessage.PayloadOneofCase.OrderSubmit:
                await HandleCallbackAsync(message,
                    m => _handler.OnOrderSubmitAsync(m.OrderSubmit, ct),
                    (mid, ack) => new ClientMessage { MessageId = mid, Mode = MessageMode.Push, OrderSubmit = ack },
                    ct).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.OrderSettle:
                await HandleCallbackAsync(message,
                    m => _handler.OnOrderSettleAsync(m.OrderSettle, ct),
                    (mid, ack) => new ClientMessage { MessageId = mid, Mode = MessageMode.Push, OrderSettle = ack },
                    ct).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.OrderRevoke:
                await HandleCallbackAsync(message,
                    m => _handler.OnOrderRevokeAsync(m.OrderRevoke, ct),
                    (mid, ack) => new ClientMessage { MessageId = mid, Mode = MessageMode.Push, OrderRevoke = ack },
                    ct).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.ChannelMemberWalletQuery:
                await HandleCallbackAsync(message,
                    m => _handler.OnChannelMemberWalletQueryAsync(m.ChannelMemberWalletQuery, ct),
                    (mid, ack) => new ClientMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberWalletQuery = ack },
                    ct).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.ChannelMemberAllWalletsQuery:
                await HandleCallbackAsync(message,
                    m => _handler.OnChannelMemberAllWalletsQueryAsync(m.ChannelMemberAllWalletsQuery, ct),
                    (mid, ack) => new ClientMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberAllWalletsQuery = ack },
                    ct).ConfigureAwait(false);
                break;

            // ===== 平台 → 客户端：期号事件（仅信息性，无需回执）=====

            case ServerMessage.PayloadOneofCase.IssueCountdown:
                await RaiseAsync(IssueCountdownReceived, message.IssueCountdown, ct, nameof(IssueCountdownReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.IssueOpening:
                await RaiseAsync(IssueOpeningReceived, message.IssueOpening, ct, nameof(IssueOpeningReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.IssueStopping:
                await RaiseAsync(IssueStoppingReceived, message.IssueStopping, ct, nameof(IssueStoppingReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.IssueDrawing:
                await RaiseAsync(IssueDrawingReceived, message.IssueDrawing, ct, nameof(IssueDrawingReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.IssueFinished:
                await RaiseAsync(IssueFinishedReceived, message.IssueFinished, ct, nameof(IssueFinishedReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.IssueCheckout:
                await RaiseAsync(IssueCheckoutReceived, message.IssueCheckout, ct, nameof(IssueCheckoutReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.IssueTerminated:
                await RaiseAsync(IssueTerminatedReceived, message.IssueTerminated, ct, nameof(IssueTerminatedReceived)).ConfigureAwait(false);
                break;

            // ===== 平台 → 客户端：直播事件（仅信息性，无需回执）=====

            case ServerMessage.PayloadOneofCase.LivekitLiveChanged:
                await RaiseAsync(LivekitLiveChangedReceived, message.LivekitLiveChanged, ct, nameof(LivekitLiveChangedReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.LivekitRoomStarted:
                await RaiseAsync(LivekitRoomStartedReceived, message.LivekitRoomStarted, ct, nameof(LivekitRoomStartedReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.LivekitRoomFinished:
                await RaiseAsync(LivekitRoomFinishedReceived, message.LivekitRoomFinished, ct, nameof(LivekitRoomFinishedReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.LivekitTrackPublished:
                await RaiseAsync(LivekitTrackPublishedReceived, message.LivekitTrackPublished, ct, nameof(LivekitTrackPublishedReceived)).ConfigureAwait(false);
                break;

            case ServerMessage.PayloadOneofCase.LivekitTrackUnpublished:
                await RaiseAsync(LivekitTrackUnpublishedReceived, message.LivekitTrackUnpublished, ct, nameof(LivekitTrackUnpublishedReceived)).ConfigureAwait(false);
                break;

            default:
                _logger.LogDebug("Ignored unsolicited server message {MessageId} ({Payload}).", message.MessageId, message.PayloadCase);
                break;
        }
    }

    private async Task HandleCallbackAsync<TAck>(
        ServerMessage message,
        Func<ServerMessage, Task<TAck>> invoke,
        Func<string, TAck, ClientMessage> buildReply,
        CancellationToken ct)
    {
        ClientMessage reply;
        try
        {
            var ack = await invoke(message).ConfigureAwait(false);
            if (ack is null)
            {
                _logger.LogError("Handler returned null for message {MessageId}.", message.MessageId);
                reply = FailureReply(message.MessageId);
            }
            else
            {
                reply = buildReply(message.MessageId, ack);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler threw for message {MessageId}; replying with SystemError.", message.MessageId);
            reply = FailureReply(message.MessageId);
        }

        try
        {
            await WriteAsync(reply, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reply for message {MessageId}.", message.MessageId);
        }
    }

    private static ClientMessage FailureReply(string messageId) => new()
    {
        MessageId = messageId,
        Mode = MessageMode.Push,
        Failure = new ResultFailure { Code = ResultErrorCode.SystemError, Message = "Internal server error." }
    };

    private async Task RaiseAsync<TArgs>(
        Func<TArgs, CancellationToken, Task>? handler,
        TArgs args,
        CancellationToken cancellationToken,
        string eventName)
    {
        if (handler is null) return;

        foreach (var subscriber in handler.GetInvocationList().Cast<Func<TArgs, CancellationToken, Task>>())
        {
            try
            {
                await subscriber(args, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscriber threw while handling event {Event}.", eventName);
            }
        }
    }

    private void TransitionTo(ConnectionState next, Exception? error = null)
    {
        if (_state == next) return;
        _state = next;

        if (next != ConnectionState.Connected) _connectedTcs = CreateSignal();

        try { _options.OnStateChange?.Invoke(next, error); } catch { /* swallow */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GamingClient));
    }

    private static TaskCompletionSource<bool> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
