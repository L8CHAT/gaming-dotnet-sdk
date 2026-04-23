namespace Feivoo.Gaming.GrpcServer;

/// <summary>
/// Shared constants that both the Gaming server's DI wiring and the Go /
/// .NET client SDKs agree on. Changing these is a wire-level break across
/// every consumer.
/// </summary>
public static class GamingMessageChannel
{
    /// <summary>
    /// Vertex messaging channel name. Must match the client SDK's
    /// <c>channelName</c> constant; drift causes every Invoke to fail with
    /// a no-handler error on this channel.
    /// </summary>
    public const string Name = "feivoo-gaming-message";
}
