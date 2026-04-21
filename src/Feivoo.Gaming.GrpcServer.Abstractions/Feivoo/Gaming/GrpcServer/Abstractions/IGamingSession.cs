using Feivoo.Gaming.Grpc;

namespace Feivoo.Gaming.GrpcServer.Abstractions;

/// <summary>
/// 单个客户端会话的服务端句柄。
/// 仅暴露服务端 <b>有权发起</b> 的下行能力（推送 / 服务端→客户端 请求）。
/// </summary>
public interface IGamingSession
{
    /// <summary>客户端在认证元数据 <c>x-tenant-id</c> 中携带的租户 / AccessId。</summary>
    string AccessId { get; }

    /// <summary>会话连接是否仍然存活。</summary>
    bool IsConnected { get; }

    /// <summary>会话级取消令牌（连接断开时取消）。</summary>
    CancellationToken SessionAborted { get; }

    // ===== 服务端单向推送：期号事件 =====

    Task PushIssueCountdownAsync(IssueCountdown event_, CancellationToken cancellationToken = default);
    Task PushIssueOpeningAsync(IssueOpening event_, CancellationToken cancellationToken = default);
    Task PushIssueStoppingAsync(IssueStopping event_, CancellationToken cancellationToken = default);
    Task PushIssueDrawingAsync(IssueDrawing event_, CancellationToken cancellationToken = default);
    Task PushIssueFinishedAsync(IssueFinished event_, CancellationToken cancellationToken = default);
    Task PushIssueCheckoutAsync(IssueCheckout event_, CancellationToken cancellationToken = default);
    Task PushIssueTerminatedAsync(IssueTerminated event_, CancellationToken cancellationToken = default);

    // ===== 服务端单向推送：直播事件 =====

    Task PushLivekitLiveChangedAsync(LivekitLiveChanged event_, CancellationToken cancellationToken = default);
    Task PushLivekitRoomStartedAsync(LivekitRoomStarted event_, CancellationToken cancellationToken = default);
    Task PushLivekitRoomFinishedAsync(LivekitRoomFinished event_, CancellationToken cancellationToken = default);
    Task PushLivekitTrackPublishedAsync(LivekitTrackPublished event_, CancellationToken cancellationToken = default);
    Task PushLivekitTrackUnpublishedAsync(LivekitTrackUnpublished event_, CancellationToken cancellationToken = default);

    // ===== 服务端 → 客户端：Request/Response（订单生命周期） =====

    /// <summary>
    /// 向客户端发起投注创建请求并等待回执。
    /// 超时由 <see cref="GamingServerOptions.RequestTimeout"/> 控制。
    /// </summary>
    Task<OrderSubmitAck> RequestOrderSubmitAsync(
        OrderSubmit command,
        CancellationToken cancellationToken = default);

    /// <summary>向客户端发起订单结算请求并等待回执。</summary>
    Task<OrderSettleAck> RequestOrderSettleAsync(
        OrderSettle command,
        CancellationToken cancellationToken = default);

    /// <summary>向客户端发起撤单请求并等待回执。</summary>
    Task<OrderRevokeAck> RequestOrderRevokeAsync(
        OrderRevoke command,
        CancellationToken cancellationToken = default);

    // ===== 服务端 → 客户端：Request/Response（钱包查询）=====

    /// <summary>
    /// 向客户端查询指定币种余额并等待回执。
    /// 钱包由第三方持有，gaming 平台不管理钱包，由平台主动发起查询。
    /// </summary>
    Task<ChannelMemberWalletQueryAck> RequestChannelMemberWalletQueryAsync(
        ChannelMemberWalletQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>向客户端查询全部币种余额并等待回执。</summary>
    Task<ChannelMemberAllWalletsQueryAck> RequestChannelMemberAllWalletsQueryAsync(
        ChannelMemberAllWalletsQuery query,
        CancellationToken cancellationToken = default);
}
