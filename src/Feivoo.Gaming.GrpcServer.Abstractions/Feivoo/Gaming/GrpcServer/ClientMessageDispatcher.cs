using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// 客户端 → 平台 命令的分发：根据扁平 <see cref="ClientMessage.PayloadOneofCase"/>
/// 路由到 <see cref="IGamingServerHandler"/>，并把结果包装为对应 <see cref="ServerMessage"/>。
/// 失败信息设置在信封级 <c>failure</c> 字段，而非嵌套在 payload 内部。
/// </summary>
internal static class ClientMessageDispatcher
{
    public static async Task<ServerMessage?> BuildReplyAsync(
        IGamingServerHandler handler,
        GamingSession session,
        ClientMessage message,
        ILogger logger,
        CancellationToken ct)
    {
        var mid = message.MessageId;

        switch (message.PayloadCase)
        {
            // ===== Channel CRUD =====
            case ClientMessage.PayloadOneofCase.ChannelCreate:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelCreateAsync(message.ChannelCreate, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelCreate = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelUpdate:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelUpdateAsync(message.ChannelUpdate, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelUpdate = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelQueryAsync(message.ChannelQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelQuery = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelList:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelListAsync(message.ChannelList, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelList = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelDelete:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelDeleteAsync(message.ChannelDelete, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelDelete = ack },
                    logger).ConfigureAwait(false);

            // ===== ChannelMessage =====
            case ClientMessage.PayloadOneofCase.ChannelMessageSubmit:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMessageSubmitAsync(message.ChannelMessageSubmit, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMessageSubmit = ack },
                    logger).ConfigureAwait(false);

            // ===== ChannelMember =====
            case ClientMessage.PayloadOneofCase.ChannelMemberEnter:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberEnterAsync(message.ChannelMemberEnter, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberEnter = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberLeave:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberLeaveAsync(message.ChannelMemberLeave, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberLeave = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberQueryAsync(message.ChannelMemberQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberQuery = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberLookup:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberLookupAsync(message.ChannelMemberLookup, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberLookup = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberTurnoverQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberTurnoverQueryAsync(message.ChannelMemberTurnoverQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberTurnoverQuery = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberRecentOrderQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberRecentOrderQueryAsync(message.ChannelMemberRecentOrderQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberRecentOrderQuery = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberRebateClaim:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberRebateClaimAsync(message.ChannelMemberRebateClaim, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberRebateClaim = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.ChannelMemberAllRebatesClaim:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnChannelMemberAllRebatesClaimAsync(message.ChannelMemberAllRebatesClaim, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, ChannelMemberAllRebatesClaim = ack },
                    logger).ConfigureAwait(false);

            // ===== WaitingOrderListQuery（商户 → 平台方向）=====
            case ClientMessage.PayloadOneofCase.WaitingOrderListQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnWaitingOrderListQueryAsync(message.WaitingOrderListQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, WaitingOrderListQuery = ack },
                    logger).ConfigureAwait(false);

            // ===== LotteryGame =====
            case ClientMessage.PayloadOneofCase.LotteryGameQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnLotteryGameQueryAsync(message.LotteryGameQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, LotteryGameQuery = ack },
                    logger).ConfigureAwait(false);

            case ClientMessage.PayloadOneofCase.LotteryGameList:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnLotteryGameListAsync(message.LotteryGameList, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, LotteryGameList = ack },
                    logger).ConfigureAwait(false);

            // ===== GameNode =====
            case ClientMessage.PayloadOneofCase.GameNodeQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnGameNodeQueryAsync(message.GameNodeQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, GameNodeQuery = ack },
                    logger).ConfigureAwait(false);

            // ===== Livekit =====
            case ClientMessage.PayloadOneofCase.LivekitTokenQuery:
                return await HandlerInvoker.SafeInvokeAsync(mid,
                    () => handler.OnLivekitTokenQueryAsync(message.LivekitTokenQuery, session, ct),
                    ack => new ServerMessage { MessageId = mid, Mode = MessageMode.Push, LivekitTokenQuery = ack },
                    logger).ConfigureAwait(false);

            // ===== 客户端对服务端请求的 Ack 回包（应已由 TryCompletePending 处理）=====
            case ClientMessage.PayloadOneofCase.OrderSubmit:
            case ClientMessage.PayloadOneofCase.OrderSettle:
            case ClientMessage.PayloadOneofCase.OrderRevoke:
            case ClientMessage.PayloadOneofCase.ChannelMemberWalletQuery:
            case ClientMessage.PayloadOneofCase.ChannelMemberAllWalletsQuery:
                logger.LogWarning(
                    "Received unmatched ack (PayloadCase={Case}) from {AccessId} (message_id={MessageId}); no pending request.",
                    message.PayloadCase, session.AccessId, mid);
                return null;

            // ===== 空 payload（可能是客户端主动上报的 failure）=====
            case ClientMessage.PayloadOneofCase.None:
                if (message.Failure is { } f)
                    logger.LogWarning(
                        "Client {AccessId} reported failure [{Code}] {Msg} with no matching request.",
                        session.AccessId, f.Code, f.Message);
                return null;

            default:
                logger.LogDebug("Ignored unknown client payload {Payload} from {AccessId}.",
                    message.PayloadCase, session.AccessId);
                return null;
        }
    }
}
