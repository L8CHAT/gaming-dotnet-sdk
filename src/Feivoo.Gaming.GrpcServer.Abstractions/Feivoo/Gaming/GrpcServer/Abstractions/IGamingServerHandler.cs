using Feivoo.Gaming.Grpc;

namespace Feivoo.Gaming.GrpcServer.Abstractions;

/// <summary>
/// 服务端业务必须实现的命令处理器（扁平信封，每个客户端操作类型独立路由）。
/// 客户端发起的每一个操作都必须返回对应的 <c>*Ack</c>，
/// 否则客户端侧的请求会一直挂起直至超时。
/// 实现抛出的异常会被服务端 SDK 统一转为信封级 <see cref="ResultFailure"/> 回写客户端，
/// 内部异常细节不会泄露到对端，仅记录到服务端日志。
/// </summary>
public interface IGamingServerHandler
{
    // ===== Channel =====

    Task<ChannelCreateAck> OnChannelCreateAsync(
        ChannelCreate command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelUpdateAck> OnChannelUpdateAsync(
        ChannelUpdate command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelQueryAck> OnChannelQueryAsync(
        ChannelQuery command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelListAck> OnChannelListAsync(
        ChannelList command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelDeleteAck> OnChannelDeleteAsync(
        ChannelDelete command, IGamingSession session, CancellationToken cancellationToken = default);

    // ===== ChannelMessage =====

    Task<ChannelMessageSubmitAck> OnChannelMessageSubmitAsync(
        ChannelMessageSubmit command, IGamingSession session, CancellationToken cancellationToken = default);

    // ===== ChannelMember =====

    Task<ChannelMemberEnterAck> OnChannelMemberEnterAsync(
        ChannelMemberEnter command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberLeaveAck> OnChannelMemberLeaveAsync(
        ChannelMemberLeave command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberQueryAck> OnChannelMemberQueryAsync(
        ChannelMemberQuery command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberLookupAck> OnChannelMemberLookupAsync(
        ChannelMemberLookup command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberTurnoverQueryAck> OnChannelMemberTurnoverQueryAsync(
        ChannelMemberTurnoverQuery command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberRecentOrderQueryAck> OnChannelMemberRecentOrderQueryAsync(
        ChannelMemberRecentOrderQuery command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberRebateClaimAck> OnChannelMemberRebateClaimAsync(
        ChannelMemberRebateClaim command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<ChannelMemberAllRebatesClaimAck> OnChannelMemberAllRebatesClaimAsync(
        ChannelMemberAllRebatesClaim command, IGamingSession session, CancellationToken cancellationToken = default);

    // ===== WaitingOrderListQuery（商户 → 平台方向，平台负责实现）=====

    Task<WaitingOrderListQueryAck> OnWaitingOrderListQueryAsync(
        WaitingOrderListQuery query, IGamingSession session, CancellationToken cancellationToken = default);

    // ===== LotteryGame =====

    Task<LotteryGameQueryAck> OnLotteryGameQueryAsync(
        LotteryGameQuery command, IGamingSession session, CancellationToken cancellationToken = default);

    Task<LotteryGameListAck> OnLotteryGameListAsync(
        LotteryGameList command, IGamingSession session, CancellationToken cancellationToken = default);

    // ===== GameNode =====

    Task<GameNodeQueryAck> OnGameNodeQueryAsync(
        GameNodeQuery command, IGamingSession session, CancellationToken cancellationToken = default);

    // ===== Livekit =====

    Task<LivekitTokenQueryAck> OnLivekitTokenQueryAsync(
        LivekitTokenQuery command, IGamingSession session, CancellationToken cancellationToken = default);
}
