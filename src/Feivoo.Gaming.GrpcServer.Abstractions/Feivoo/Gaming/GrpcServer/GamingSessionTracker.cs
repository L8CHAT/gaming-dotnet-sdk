using System.Collections.Concurrent;
using Feivoo.Gaming.GrpcServer.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vertex.Messaging;
using Vertex.Transport;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// Background service that tracks live Gaming sessions by listening to the
/// Vertex transport's PeerConnectionChanged events. On Connected it creates
/// a <see cref="VertexGamingSession"/> and invokes
/// <see cref="GamingServerOptions.OnSessionConnected"/>; on Disconnected it
/// marks the session dead and invokes <see cref="GamingServerOptions.OnSessionDisconnected"/>.
///
/// Host apps typically wire <c>OnSessionConnected</c> /
/// <c>OnSessionDisconnected</c> to an <c>IMerchantSessionRegistry</c> so
/// business code can look up a session by AccessId.
/// </summary>
internal sealed class GamingSessionTracker : IHostedService, IDisposable
{
    private readonly ITransport _transport;
    private readonly IMessageBus _bus;
    private readonly IRpcClient _rpc;
    private readonly GamingServerOptions _options;
    private readonly ILogger<GamingSessionTracker> _logger;
    private readonly ConcurrentDictionary<PeerId, VertexGamingSession> _sessions = new();
    private EventHandler<PeerConnectionEvent>? _handler;

    public GamingSessionTracker(
        ITransportRegistry transports,
        [FromKeyedServices(GamingMessageChannel.Name)] IMessageBus bus,
        [FromKeyedServices(GamingMessageChannel.Name)] IRpcClient rpc,
        IOptions<GamingServerOptions> options,
        ILogger<GamingSessionTracker> logger)
    {
        _transport = transports.Get(GamingMessageChannel.Name);
        _bus = bus;
        _rpc = rpc;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = OnPeerConnectionChanged;
        _transport.PeerConnectionChanged += _handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null)
        {
            _transport.PeerConnectionChanged -= _handler;
            _handler = null;
        }
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();

    private void OnPeerConnectionChanged(object? sender, PeerConnectionEvent e)
    {
        try
        {
            switch (e.State)
            {
                case PeerConnectionState.Connected:
                    // AccessId == PeerId.Value because the SDK sets both
                    // x-tenant-id and x-vertex-peer-id to the same value.
                    var session = new VertexGamingSession(e.Peer.Value, e.Peer, _bus, _rpc);
                    _sessions[e.Peer] = session;
                    _options.OnSessionConnected?.Invoke(session);
                    break;

                case PeerConnectionState.Disconnected:
                    if (_sessions.TryRemove(e.Peer, out var removed))
                    {
                        removed.MarkDisconnected();
                        _options.OnSessionDisconnected?.Invoke(removed, null);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GamingSessionTracker callback failed for {Peer} {State}", e.Peer, e.State);
        }
    }
}
