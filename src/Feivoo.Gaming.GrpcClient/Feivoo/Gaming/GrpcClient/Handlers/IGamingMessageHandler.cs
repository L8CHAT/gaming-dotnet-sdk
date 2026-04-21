using Feivoo.Gaming.Grpc;

namespace Feivoo.Gaming.GrpcClient.Handlers;

/// <summary>
/// 平台 → 客户端 的 <b>必须处理</b> 的下行业务回调。
/// 平台会下发投注创建、结算、撤单等订单指令以及钱包查询请求，要求客户端给出明确响应；
/// 若不处理，平台侧会因收不到回执而出现状态异常，因此本接口的所有方法都是必需的。
/// 实现抛出的异常会被 SDK 自动转为 <see cref="ResultErrorCode.SystemError"/> 失败响应并回写平台。
///
/// 仅信息性的下行（期号事件、直播状态等）通过 <see cref="GamingClient"/>
/// 上的事件暴露，调用方可按需订阅。
/// </summary>
public interface IGamingMessageHandler
{
    /// <summary>
    /// 处理投注创建请求。返回的 <see cref="OrderSubmitAck"/>（含 <c>order_id</c> 与余额）
    /// 由 SDK 自动以同一 <c>message_id</c> 回写平台。
    /// </summary>
    Task<OrderSubmitAck> OnOrderSubmitAsync(OrderSubmit command, CancellationToken cancellationToken = default);

    /// <summary>处理订单结算请求。返回结算后余额。</summary>
    Task<OrderSettleAck> OnOrderSettleAsync(OrderSettle command, CancellationToken cancellationToken = default);

    /// <summary>处理撤单请求。返回撤单后余额。</summary>
    Task<OrderRevokeAck> OnOrderRevokeAsync(OrderRevoke command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 平台向客户端查询指定币种余额。
    /// 钱包由第三方持有，gaming 平台不管理用户钱包，因此由平台主动发起查询，客户端回包。
    /// </summary>
    Task<ChannelMemberWalletQueryAck> OnChannelMemberWalletQueryAsync(ChannelMemberWalletQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 平台向客户端查询全部币种余额。
    /// 钱包由第三方持有，gaming 平台不管理用户钱包，因此由平台主动发起查询，客户端回包。
    /// </summary>
    Task<ChannelMemberAllWalletsQueryAck> OnChannelMemberAllWalletsQueryAsync(ChannelMemberAllWalletsQuery query, CancellationToken cancellationToken = default);
}
