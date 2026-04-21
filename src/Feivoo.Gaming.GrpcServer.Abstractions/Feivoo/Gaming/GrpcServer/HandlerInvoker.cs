using Feivoo.Gaming.Grpc;
using Microsoft.Extensions.Logging;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// 把 handler 的返回结果统一包装：null 或抛异常都转成信封级 <see cref="ResultFailure"/>。
/// 内部异常不外泄，仅记录到日志。
/// </summary>
internal static class HandlerInvoker
{
    public static async Task<ServerMessage> SafeInvokeAsync<TAck>(
        string messageId,
        Func<Task<TAck>> invoke,
        Func<TAck, ServerMessage> buildReply,
        ILogger logger)
    {
        try
        {
            var ack = await invoke().ConfigureAwait(false);
            return ack is not null ? buildReply(ack) : Fail(messageId, "Handler returned null.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handler threw; replying with SystemError.");
            return Fail(messageId, "Internal server error.");
        }
    }

    private static ServerMessage Fail(string messageId, string message) => new()
    {
        MessageId = messageId,
        Mode = MessageMode.Push,
        Failure = new ResultFailure { Code = ResultErrorCode.SystemError, Message = message }
    };
}
