using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Vertex.Messaging;
using Vertex.Transport;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// <see cref="IGamingSession"/> backed by a Vertex <see cref="IMessageBus"/>
/// + <see cref="IRpcClient"/> pair, scoped to a single connected peer. Every
/// push and request targets the same <see cref="PeerId"/>; the per-peer
/// isolation is provided by Vertex's transport layer.
/// </summary>
internal sealed class VertexGamingSession : IGamingSession
{
    private readonly IMessageBus _bus;
    private readonly IRpcClient _rpc;
    private readonly PeerId _peerId;
    private int _disconnected;

    public VertexGamingSession(string accessId, PeerId peerId, IMessageBus bus, IRpcClient rpc)
    {
        AccessId = accessId;
        _peerId = peerId;
        _bus = bus;
        _rpc = rpc;
    }

    public string AccessId { get; }

    public bool IsConnected => Volatile.Read(ref _disconnected) == 0;

    /// <summary>Called by the session registry when the transport emits Disconnected.</summary>
    internal void MarkDisconnected() => Interlocked.Exchange(ref _disconnected, 1);

    // ── Push events (IMessageBus.PublishAsync) ────────────────────────────

    public Task PushIssueCountdownAsync(IssueCountdown e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushIssueOpeningAsync(IssueOpening e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushIssueStoppingAsync(IssueStopping e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushIssueDrawingAsync(IssueDrawing e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushIssueFinishedAsync(IssueFinished e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushIssueCheckoutAsync(IssueCheckout e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushIssueTerminatedAsync(IssueTerminated e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();

    public Task PushLivekitLiveChangedAsync(LivekitLiveChanged e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushLivekitRoomStartedAsync(LivekitRoomStarted e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushLivekitRoomFinishedAsync(LivekitRoomFinished e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushLivekitTrackPublishedAsync(LivekitTrackPublished e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();
    public Task PushLivekitTrackUnpublishedAsync(LivekitTrackUnpublished e, CancellationToken ct = default) => _bus.PublishAsync(e, _peerId, ct).AsTask();

    // ── Request/response (IRpcClient.InvokeAsync) ─────────────────────────

    public Task<OrderSubmitAck> RequestOrderSubmitAsync(OrderSubmit req, TimeSpan? timeout = null, CancellationToken ct = default)
        => _rpc.InvokeAsync<OrderSubmit, OrderSubmitAck>(req, _peerId, timeout, ct).AsTask();

    public Task<OrderSettleAck> RequestOrderSettleAsync(OrderSettle req, TimeSpan? timeout = null, CancellationToken ct = default)
        => _rpc.InvokeAsync<OrderSettle, OrderSettleAck>(req, _peerId, timeout, ct).AsTask();

    public Task<OrderRevokeAck> RequestOrderRevokeAsync(OrderRevoke req, TimeSpan? timeout = null, CancellationToken ct = default)
        => _rpc.InvokeAsync<OrderRevoke, OrderRevokeAck>(req, _peerId, timeout, ct).AsTask();

    public Task<ChannelMemberWalletQueryAck> RequestChannelMemberWalletQueryAsync(ChannelMemberWalletQuery req, TimeSpan? timeout = null, CancellationToken ct = default)
        => _rpc.InvokeAsync<ChannelMemberWalletQuery, ChannelMemberWalletQueryAck>(req, _peerId, timeout, ct).AsTask();

    public Task<ChannelMemberAllWalletsQueryAck> RequestChannelMemberAllWalletsQueryAsync(ChannelMemberAllWalletsQuery req, TimeSpan? timeout = null, CancellationToken ct = default)
        => _rpc.InvokeAsync<ChannelMemberAllWalletsQuery, ChannelMemberAllWalletsQueryAck>(req, _peerId, timeout, ct).AsTask();
}
