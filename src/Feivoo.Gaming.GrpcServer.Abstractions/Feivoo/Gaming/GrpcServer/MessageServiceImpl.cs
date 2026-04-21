using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.Grpc.Messaging;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// 内部 gRPC 服务实现：负责认证、信封读写、请求-响应关联与异常包装。
/// 标记为 <c>internal</c>：业务方不可继承、替换或直接实例化，必须经由
/// <c>endpoints.MapFeivooGamingServer()</c> 挂载。
/// </summary>
internal sealed class MessageServiceImpl : MessageService.MessageServiceBase
{
    private readonly IGamingServerHandler _handler;
    private readonly IGamingServerAuthenticator _authenticator;
    private readonly GamingServerOptions _options;
    private readonly ILogger<MessageServiceImpl> _logger;

    public MessageServiceImpl(
        IGamingServerHandler handler,
        IGamingServerAuthenticator authenticator,
        IOptions<GamingServerOptions> options,
        ILogger<MessageServiceImpl>? logger = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<MessageServiceImpl>.Instance;
    }

    public override async Task Connect(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        // 1) 认证
        var (accessId, secretKey) = AuthHeaders.Read(context.RequestHeaders);
        var principal = await _authenticator
            .AuthenticateAsync(accessId, secretKey, context.CancellationToken)
            .ConfigureAwait(false);

        if (principal is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid x-tenant-id / x-secret-key."));
        }

        // 2) 建立会话
        var session = new GamingSession(
            principal,
            responseStream,
            _options.RequestTimeout,
            context.CancellationToken,
            _logger);

        try
        {
            _options.OnSessionConnected?.Invoke(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnSessionConnected callback threw for {AccessId}.", session.AccessId);
        }

        Exception? terminalError = null;
        try
        {
            // 3) 读循环
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                message.MessageId = MessageIdGenerator.Ensure(message.MessageId);

                // 与服务端发起的请求做关联
                if (session.TryCompletePending(message)) continue;

                _ = DispatchAsync(session, message, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            terminalError = ex;
            _logger.LogError(ex, "Receive loop terminated for {AccessId}.", session.AccessId);
        }
        finally
        {
            session.FailAllPending(
                terminalError ?? new OperationCanceledException("Session aborted."));
            try
            {
                _options.OnSessionDisconnected?.Invoke(session, terminalError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnSessionDisconnected callback threw for {AccessId}.", session.AccessId);
            }
        }
    }

    private async Task DispatchAsync(GamingSession session, ClientMessage message, CancellationToken ct)
    {
        try
        {
            var reply = await ClientMessageDispatcher
                .BuildReplyAsync(_handler, session, message, _logger, ct)
                .ConfigureAwait(false);
            if (reply is null) return;
            await session.SendAsync(reply, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reply for message {MessageId} ({Payload}).",
                message.MessageId, message.PayloadCase);
        }
    }
}
