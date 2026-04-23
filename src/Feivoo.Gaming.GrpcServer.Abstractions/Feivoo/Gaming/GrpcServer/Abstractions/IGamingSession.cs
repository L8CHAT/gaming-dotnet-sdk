using Feivoo.Gaming.Grpc;

namespace Feivoo.Gaming.GrpcServer.Abstractions;

/// <summary>
/// Per-connection server handle. Only exposes the server-initiated down-
/// stream capabilities (fire-and-forget pushes + request/response Invokes).
/// Clients use the symmetric <c>Subscribe</c> / <c>HandleRequest</c> paths
/// on their Vertex channel to receive them.
///
/// Obtain via <c>IMerchantSessionRegistry</c> (or whatever the host app
/// registers as the session store) — the registry is populated by the
/// Vertex transport's PeerConnectionChanged event once auth succeeds.
/// </summary>
public interface IGamingSession
{
    /// <summary>
    /// Caller's tenant id from <c>x-tenant-id</c> metadata (same as the
    /// Vertex PeerId, since the SDK sends both to the same value).
    /// </summary>
    string AccessId { get; }

    /// <summary>True while the underlying Vertex stream is live for this peer.</summary>
    bool IsConnected { get; }

    // ── Server → client unidirectional pushes (issue events) ──────────────

    Task PushIssueCountdownAsync(IssueCountdown event_, CancellationToken cancellationToken = default);
    Task PushIssueOpeningAsync(IssueOpening event_, CancellationToken cancellationToken = default);
    Task PushIssueStoppingAsync(IssueStopping event_, CancellationToken cancellationToken = default);
    Task PushIssueDrawingAsync(IssueDrawing event_, CancellationToken cancellationToken = default);
    Task PushIssueFinishedAsync(IssueFinished event_, CancellationToken cancellationToken = default);
    Task PushIssueCheckoutAsync(IssueCheckout event_, CancellationToken cancellationToken = default);
    Task PushIssueTerminatedAsync(IssueTerminated event_, CancellationToken cancellationToken = default);

    // ── Server → client unidirectional pushes (livekit events) ────────────

    Task PushLivekitLiveChangedAsync(LivekitLiveChanged event_, CancellationToken cancellationToken = default);
    Task PushLivekitRoomStartedAsync(LivekitRoomStarted event_, CancellationToken cancellationToken = default);
    Task PushLivekitRoomFinishedAsync(LivekitRoomFinished event_, CancellationToken cancellationToken = default);
    Task PushLivekitTrackPublishedAsync(LivekitTrackPublished event_, CancellationToken cancellationToken = default);
    Task PushLivekitTrackUnpublishedAsync(LivekitTrackUnpublished event_, CancellationToken cancellationToken = default);

    // ── Server → client request/response Invokes ─────────────────────────

    Task<OrderSubmitAck> RequestOrderSubmitAsync(
        OrderSubmit request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<OrderSettleAck> RequestOrderSettleAsync(
        OrderSettle request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<OrderRevokeAck> RequestOrderRevokeAsync(
        OrderRevoke request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<ChannelMemberWalletQueryAck> RequestChannelMemberWalletQueryAsync(
        ChannelMemberWalletQuery request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<ChannelMemberAllWalletsQueryAck> RequestChannelMemberAllWalletsQueryAsync(
        ChannelMemberAllWalletsQuery request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
