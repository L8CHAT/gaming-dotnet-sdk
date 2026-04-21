namespace Feivoo.Gaming.GrpcServer;

/// <summary>认证通过后的会话主体。</summary>
public sealed class GamingPrincipal
{
    public GamingPrincipal(string accessId, object? state = null)
    {
        if (string.IsNullOrWhiteSpace(accessId))
        {
            throw new ArgumentException("AccessId is required.", nameof(accessId));
        }
        AccessId = accessId;
        State = state;
    }

    public string AccessId { get; }

    /// <summary>业务自定义会话状态（如租户配置缓存等），可选。</summary>
    public object? State { get; }
}
