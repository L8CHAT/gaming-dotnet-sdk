using Grpc.Core;

namespace Feivoo.Gaming.GrpcServer;

/// <summary>gRPC metadata 头名 + 解析工具。</summary>
internal static class AuthHeaders
{
    public const string TenantHeader = "x-tenant-id";
    public const string SecretHeader = "x-secret-key";

    public static (string AccessId, string SecretKey) Read(Metadata headers)
    {
        string? accessId = null;
        string? secretKey = null;
        foreach (var entry in headers)
        {
            if (string.Equals(entry.Key, TenantHeader, StringComparison.OrdinalIgnoreCase)) accessId = entry.Value;
            else if (string.Equals(entry.Key, SecretHeader, StringComparison.OrdinalIgnoreCase)) secretKey = entry.Value;
        }
        return (accessId ?? string.Empty, secretKey ?? string.Empty);
    }
}
