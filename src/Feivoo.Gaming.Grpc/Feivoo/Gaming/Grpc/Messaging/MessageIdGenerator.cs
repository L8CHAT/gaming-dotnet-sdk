namespace Feivoo.Gaming.Grpc.Messaging;

public static class MessageIdGenerator
{
    public static string Ensure(string? current, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current!;
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback!;
        }

        return Guid.NewGuid().ToString("N");
    }
}
