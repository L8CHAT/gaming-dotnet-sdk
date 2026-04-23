using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.Grpc.Messaging;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>内部会话实现：持有双向流写端，向业务方仅暴露 <see cref="IGamingSession"/>。</summary>
internal sealed class GamingSession : IGamingSession
{
    private readonly IServerStreamWriter<ServerMessage> _responseStream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly RequestResponseCoordinator<ClientMessage> _pending = new();
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger _logger;

    public GamingSession(
        GamingPrincipal principal,
        IServerStreamWriter<ServerMessage> responseStream,
        TimeSpan requestTimeout,
        CancellationToken sessionAborted,
        ILogger logger)
    {
        Principal = principal;
        _responseStream = responseStream;
        _requestTimeout = requestTimeout;
        SessionAborted = sessionAborted;
        _logger = logger;
    }

    public GamingPrincipal Principal { get; }

    public string AccessId => Principal.AccessId;

    public bool IsConnected => !SessionAborted.IsCancellationRequested;

    public CancellationToken SessionAborted { get; }

    // ===== 单向推送：期号事件 =====

    public Task PushIssueCountdownAsync(IssueCountdown event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueCountdown = event_), cancellationToken);

    public Task PushIssueOpeningAsync(IssueOpening event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueOpening = event_), cancellationToken);

    public Task PushIssueStoppingAsync(IssueStopping event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueStopping = event_), cancellationToken);

    public Task PushIssueDrawingAsync(IssueDrawing event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueDrawing = event_), cancellationToken);

    public Task PushIssueFinishedAsync(IssueFinished event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueFinished = event_), cancellationToken);

    public Task PushIssueCheckoutAsync(IssueCheckout event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueCheckout = event_), cancellationToken);

    public Task PushIssueTerminatedAsync(IssueTerminated event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.IssueTerminated = event_), cancellationToken);

    // ===== 单向推送：直播事件 =====

    public Task PushLivekitLiveChangedAsync(LivekitLiveChanged event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.LivekitLiveChanged = event_), cancellationToken);

    public Task PushLivekitRoomStartedAsync(LivekitRoomStarted event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.LivekitRoomStarted = event_), cancellationToken);

    public Task PushLivekitRoomFinishedAsync(LivekitRoomFinished event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.LivekitRoomFinished = event_), cancellationToken);

    public Task PushLivekitTrackPublishedAsync(LivekitTrackPublished event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.LivekitTrackPublished = event_), cancellationToken);

    public Task PushLivekitTrackUnpublishedAsync(LivekitTrackUnpublished event_, CancellationToken cancellationToken = default)
        => SendAsync(Push(s => s.LivekitTrackUnpublished = event_), cancellationToken);

    // ===== Server → Client Request/Response：订单生命周期 =====

    public Task<OrderSubmitAck> RequestOrderSubmitAsync(OrderSubmit command, CancellationToken cancellationToken = default)
        => RequestClientAsync(
            Req(s => s.OrderSubmit = command),
            r => r.OrderSubmit,
            cancellationToken);

    public Task<OrderSettleAck> RequestOrderSettleAsync(OrderSettle command, CancellationToken cancellationToken = default)
        => RequestClientAsync(
            Req(s => s.OrderSettle = command),
            r => r.OrderSettle,
            cancellationToken);

    public Task<OrderRevokeAck> RequestOrderRevokeAsync(OrderRevoke command, CancellationToken cancellationToken = default)
        => RequestClientAsync(
            Req(s => s.OrderRevoke = command),
            r => r.OrderRevoke,
            cancellationToken);

    // ===== Server → Client Request/Response：钱包查询 =====

    public Task<ChannelMemberWalletQueryAck> RequestChannelMemberWalletQueryAsync(ChannelMemberWalletQuery query, CancellationToken cancellationToken = default)
        => RequestClientAsync(
            Req(s => s.ChannelMemberWalletQuery = query),
            r => r.ChannelMemberWalletQuery,
            cancellationToken);

    public Task<ChannelMemberAllWalletsQueryAck> RequestChannelMemberAllWalletsQueryAsync(ChannelMemberAllWalletsQuery query, CancellationToken cancellationToken = default)
        => RequestClientAsync(
            Req(s => s.ChannelMemberAllWalletsQuery = query),
            r => r.ChannelMemberAllWalletsQuery,
            cancellationToken);

    // ===== 内部：核心 Request 助手 =====

    private async Task<TAck> RequestClientAsync<TAck>(
        ServerMessage envelope,
        Func<ClientMessage, TAck?> extract,
        CancellationToken cancellationToken)
    {
        var reply = await _pending.SendAndWaitAsync(
            envelope.MessageId,
            () => SendAsync(envelope, cancellationToken),
            _requestTimeout,
            cancellationToken).ConfigureAwait(false);

        if (reply.Failure is { } f)
            throw new InvalidOperationException($"[{f.Code}] {f.Message}");

        return extract(reply)
               ?? throw new InvalidOperationException(
                   $"Client reply did not contain expected payload, got '{reply.PayloadCase}'.");
    }

    private static ServerMessage Push(Action<ServerMessage> set)
    {
        var msg = new ServerMessage { MessageId = MessageIdGenerator.Ensure(null), Mode = MessageMode.Push };
        set(msg);
        return msg;
    }

    private static ServerMessage Req(Action<ServerMessage> set)
    {
        var msg = new ServerMessage { MessageId = MessageIdGenerator.Ensure(null), Mode = MessageMode.Request };
        set(msg);
        return msg;
    }

    // ===== 内部：分发与写出 =====

    internal bool TryCompletePending(ClientMessage message)
        => _pending.TryComplete(message.MessageId, message);

    internal void FailAllPending(Exception exception)
        => _pending.FailAll(exception);

    internal async Task SendAsync(ServerMessage message, CancellationToken cancellationToken)
    {
        // Wait on the lock honouring the caller's cancellation token; this is
        // safe because the message has not yet touched the wire.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // IMPORTANT: do NOT pass cancellationToken to WriteAsync. If the
            // token cancels mid-write, gRPC will emit a RST_STREAM on the
            // underlying HTTP/2 stream, permanently breaking this bidi
            // session for every concurrent caller — a single timed-out
            // request would silently kill the entire connection. Once we
            // hold _writeLock the message must be flushed to completion;
            // any actual stream-level termination will surface to the read
            // loop separately.
            await _responseStream.WriteAsync(message).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
