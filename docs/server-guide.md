# 服务端接入指南

## 1. 安装

```xml
<PackageReference Include="Feivoo.Gaming.GrpcServer.Abstractions" Version="1.0.0" />
```

ASP.NET Core 还需要：

```xml
<PackageReference Include="Grpc.AspNetCore" Version="2.71.0" />
```

---

## 2. 快速接入

### 步骤概览

1. 实现 `IGamingServerAuthenticator`（验证客户端身份）
2. 实现 `IGamingServerHandler`（处理 20 个业务操作）
3. 注册 DI
4. 挂载 gRPC endpoint

### 最小示例

```csharp
// Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeivooGamingServer<MyServerHandler, MyAuthenticator>(options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(15);

    options.OnSessionConnected = session =>
        Console.WriteLine($"[+] 客户端连接: {session.AccessId}");

    options.OnSessionDisconnected = (session, ex) =>
        Console.WriteLine($"[-] 客户端断开: {session.AccessId}" + (ex is null ? "" : $" ({ex.Message})"));
});

var app = builder.Build();
app.MapFeivooGamingServer();
app.Run();
```

---

## 3. 实现认证器

```csharp
using Feivoo.Gaming.GrpcServer;
using Feivoo.Gaming.GrpcServer.Abstractions;

public class MyAuthenticator : IGamingServerAuthenticator
{
    private readonly IClientCredentialStore _store;

    public MyAuthenticator(IClientCredentialStore store)
        => _store = store;

    /// <summary>
    /// 校验客户端在 gRPC Metadata 中携带的凭证。
    /// 返回 null 表示拒绝连接，SDK 会回复 Unauthenticated 状态码。
    /// </summary>
    public async Task<GamingPrincipal?> AuthenticateAsync(
        string accessId,
        string secretKey,
        CancellationToken cancellationToken)
    {
        var valid = await _store.ValidateAsync(accessId, secretKey, cancellationToken);
        if (!valid) return null;

        return new GamingPrincipal
        {
            AccessId = accessId,
            // State 可存储任意业务状态，会话期间可通过 session.Principal.State 访问
            State = await _store.LoadTenantInfoAsync(accessId, cancellationToken)
        };
    }
}
```

客户端在 gRPC Metadata 中携带：
- `x-tenant-id`：AccessId
- `x-secret-key`：密钥

---

## 4. 实现业务处理器

`IGamingServerHandler` 共 20 个方法，全部必须实现。
每个方法都接收对应的子操作对象和 `IGamingSession`（当前客户端会话句柄）。

```csharp
using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.GrpcServer.Abstractions;

public class MyServerHandler : IGamingServerHandler
{
    private readonly IChannelService _channels;
    private readonly IMemberService _members;

    public MyServerHandler(IChannelService channels, IMemberService members)
    {
        _channels = channels;
        _members  = members;
    }

    // ===== Channel =====

    public async Task<ChannelCreateResult> OnChannelCreateAsync(
        ChannelCreate cmd, IGamingSession session, CancellationToken ct)
    {
        var channel = await _channels.CreateAsync(cmd.Name, session.AccessId, ct);
        return new ChannelCreateResult { ChannelId = channel.Id };
    }

    public async Task<ChannelUpdateResult> OnChannelUpdateAsync(
        ChannelUpdate cmd, IGamingSession session, CancellationToken ct)
    {
        await _channels.UpdateAsync(cmd.ChannelId, cmd.Name, ct);
        return new ChannelUpdateResult();
    }

    public async Task<ChannelQueryResult> OnChannelQueryAsync(
        ChannelQuery cmd, IGamingSession session, CancellationToken ct)
    {
        var channel = await _channels.GetAsync(cmd.ChannelId, ct);
        return new ChannelQueryResult { Channel = channel.ToProto() };
    }

    public async Task<ChannelListResult> OnChannelListAsync(
        ChannelList cmd, IGamingSession session, CancellationToken ct)
    {
        var channels = await _channels.ListAsync(ct);
        var result = new ChannelListResult();
        result.Channels.AddRange(channels.Select(c => c.ToProto()));
        return result;
    }

    public async Task<ChannelDeleteResult> OnChannelDeleteAsync(
        ChannelDelete cmd, IGamingSession session, CancellationToken ct)
    {
        await _channels.DeleteAsync(cmd.ChannelId, ct);
        return new ChannelDeleteResult();
    }

    // ===== ChannelMember =====

    public async Task<ChannelMemberEnterResult> OnChannelMemberEnterAsync(
        ChannelMemberEnter cmd, IGamingSession session, CancellationToken ct)
    {
        await _members.EnterAsync(cmd.ChannelId, cmd.PlayerId, ct);
        return new ChannelMemberEnterResult();
    }

    public async Task<ChannelMemberLeaveResult> OnChannelMemberLeaveAsync(
        ChannelMemberLeave cmd, IGamingSession session, CancellationToken ct)
    {
        await _members.LeaveAsync(cmd.ChannelId, cmd.PlayerId, ct);
        return new ChannelMemberLeaveResult();
    }

    public async Task<ChannelMemberQueryResult> OnChannelMemberQueryAsync(
        ChannelMemberQuery cmd, IGamingSession session, CancellationToken ct)
        => new ChannelMemberQueryResult { Member = (await _members.QueryAsync(cmd.ChannelId, cmd.PlayerId, ct)).ToProto() };

    public async Task<ChannelMemberLookupResult> OnChannelMemberLookupAsync(
        ChannelMemberLookup cmd, IGamingSession session, CancellationToken ct)
        => new ChannelMemberLookupResult { Member = (await _members.LookupAsync(cmd.PlayerId, ct)).ToProto() };

    public Task<ChannelMemberWalletQueryResult> OnChannelMemberWalletQueryAsync(
        ChannelMemberWalletQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<ChannelMemberAllWalletsQueryResult> OnChannelMemberAllWalletsQueryAsync(
        ChannelMemberAllWalletsQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<ChannelMemberTurnoverQueryResult> OnChannelMemberTurnoverQueryAsync(
        ChannelMemberTurnoverQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<ChannelMemberRecentOrderQueryResult> OnChannelMemberRecentOrderQueryAsync(
        ChannelMemberRecentOrderQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<ChannelMemberRebateClaimResult> OnChannelMemberRebateClaimAsync(
        ChannelMemberRebateClaim cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<ChannelMemberAllRebatesClaimResult> OnChannelMemberAllRebatesClaimAsync(
        ChannelMemberAllRebatesClaim cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    // ===== ChannelMessage =====

    public async Task<ChannelMessageSubmitResult> OnChannelMessageSubmitAsync(
        ChannelMessageSubmit cmd, IGamingSession session, CancellationToken ct)
    {
        // 存储消息，然后广播给其他在线会话
        await BroadcastAsync(cmd.ChannelId, cmd.Content, session, ct);
        return new ChannelMessageSubmitResult();
    }

    // ===== LotteryGame =====

    public Task<LotteryGameQueryResult> OnLotteryGameQueryAsync(
        LotteryGameQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<LotteryGameListResult> OnLotteryGameListAsync(
        LotteryGameList cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    // ===== GameNode =====

    public Task<GameNodeQueryResult> OnGameNodeQueryAsync(
        GameNodeQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    // ===== Livekit =====

    public Task<LivekitTokenQueryResult> OnLivekitTokenQueryAsync(
        LivekitTokenQuery cmd, IGamingSession session, CancellationToken ct)
        => throw new NotImplementedException();

    // ===== 私有辅助方法 =====

    private async Task BroadcastAsync(
        string channelId, string content, IGamingSession sender, CancellationToken ct)
    {
        // 示例：向同频道内的其他会话推送消息
        // 实际场景下通过 ISessionRegistry 获取其他会话
        await Task.CompletedTask;
    }
}
```

### 错误处理

处理器方法抛出的**任何异常**都会被 `HandlerInvoker.SafeInvokeAsync` 捕获：
- 异常信息包装为 `ResultFailure { Code = SystemError, Message = "Handler threw ..." }`
- 堆栈细节仅记录到服务端日志，**不会**传输到客户端
- 客户端收到的是 `ResultFailure`，可通过 `result.Failure` 判断

---

## 5. 使用 IGamingSession 主动下行

每个 `IGamingServerHandler` 方法都接收 `IGamingSession session`，
通过它可以向该客户端**主动发起**推送或请求：

### 服务端推送（不等待客户端确认）

```csharp
// 推送频道消息
await session.PushChannelMessageAsync(new ChannelMessageResult
{
    ChannelId = "ch-001",
    Content   = "欢迎加入频道！",
    SenderId  = "system"
}, cancellationToken);

// 推送直播状态
await session.PushLivekitStatusAsync(new LivekitStatus
{
    ChannelId = "ch-001",
    IsLive    = true
}, cancellationToken);

// 推送期号更新
await session.PushIssueUpdateAsync(new IssueUpdate
{
    IssueNumber = "2026042001",
    WinNumbers  = "01 02 03 04 05"
}, cancellationToken);
```

### 服务端请求（等待客户端回执）

服务端可以主动向客户端发起订单相关操作并等待客户端处理结果：

```csharp
// 要求客户端创建订单
var submitResult = await session.RequestOrderSubmitAsync(new OrderSubmit
{
    GameId  = "game-001",
    Amount  = 100,
    IssueId = "2026042001"
}, cancellationToken);

if (submitResult.Failure is not null)
{
    // 客户端处理失败
    logger.LogWarning("客户端创建订单失败: {Error}", submitResult.Failure.Message);
    return;
}

// 结算订单
var settleResult = await session.RequestOrderSettleAsync(new OrderSettle
{
    OrderId   = submitResult.OrderId,
    WinAmount = 200
}, cancellationToken);

// 撤销订单
var revokeResult = await session.RequestOrderRevokeAsync(new OrderRevoke
{
    OrderId = submitResult.OrderId,
    Reason  = "游戏取消"
}, cancellationToken);

// 查询待结算订单
var listResult = await session.RequestWaitingOrderListAsync(
    new WaitingOrderList { GameId = "game-001" },
    cancellationToken);
```

超时由 `GamingServerOptions.RequestTimeout` 控制（默认 10 秒）。

---

## 6. 会话生命周期回调

```csharp
builder.Services.AddFeivooGamingServer<MyServerHandler, MyAuthenticator>(options =>
{
    options.OnSessionConnected = session =>
    {
        // 认证通过后触发，此时会话已建立
        // 注意：这是同步回调，耗时操作请使用 Task.Run 或异步事件总线
        Console.WriteLine($"[+] {session.AccessId} 连接，IsConnected={session.IsConnected}");
    };

    options.OnSessionDisconnected = (session, ex) =>
    {
        // 连接断开时触发，ex 为导致断开的异常（正常断开时为 null）
        Console.WriteLine($"[-] {session.AccessId} 断开" + (ex is null ? "（正常）" : $"（异常: {ex.Message}）"));
    };
});
```

### 会话存活检查

```csharp
public class MyServerHandler : IGamingServerHandler
{
    public async Task<ChannelCreateResult> OnChannelCreateAsync(
        ChannelCreate cmd, IGamingSession session, CancellationToken ct)
    {
        // 检查会话是否仍然存活
        if (!session.IsConnected) throw new InvalidOperationException("会话已断开。");

        // 使用会话级取消令牌
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, session.SessionAborted);

        var channel = await _channels.CreateAsync(cmd.Name, linkedCts.Token);
        return new ChannelCreateResult { ChannelId = channel.Id };
    }
}
```

`session.SessionAborted` 在连接断开时取消，用于及时中止与会话相关的异步操作。

---

## 7. 向多个会话广播

`IGamingSession` 仅代表单个连接，广播需要在业务层维护会话注册表：

```csharp
// 示例：简单的会话注册表
public class SessionRegistry
{
    private readonly ConcurrentDictionary<string, IGamingSession> _sessions = new();

    public void Register(IGamingSession session)
        => _sessions[session.AccessId] = session;

    public void Unregister(IGamingSession session)
        => _sessions.TryRemove(session.AccessId, out _);

    public IEnumerable<IGamingSession> GetByChannel(string channelId)
        => _sessions.Values.Where(s => s.IsConnected /* 加频道归属判断 */);
}

// 在 OnSessionConnected / OnSessionDisconnected 中注册/注销
options.OnSessionConnected    = session => registry.Register(session);
options.OnSessionDisconnected = (session, _) => registry.Unregister(session);

// 在 Handler 中广播
public async Task<ChannelMessageSubmitResult> OnChannelMessageSubmitAsync(
    ChannelMessageSubmit cmd, IGamingSession session, CancellationToken ct)
{
    var msg = new ChannelMessageResult { Content = cmd.Content, SenderId = session.AccessId };
    var tasks = _registry.GetByChannel(cmd.ChannelId)
        .Where(s => s.AccessId != session.AccessId)
        .Select(s => s.PushChannelMessageAsync(msg, ct));
    await Task.WhenAll(tasks);
    return new ChannelMessageSubmitResult();
}
```

---

## 8. 完整 Program.cs 示例

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 基础设施
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddScoped<IChannelService, ChannelService>();
builder.Services.AddScoped<IMemberService, MemberService>();

// Feivoo Gaming 服务端 SDK
builder.Services.AddFeivooGamingServer<MyServerHandler, MyAuthenticator>(options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(15);

    options.OnSessionConnected = session =>
    {
        var registry = session.GetService<SessionRegistry>(); // 若 Principal.State 存了 IServiceProvider
        // 或从静态/DI 容器获取 registry
    };

    options.OnSessionDisconnected = (session, ex) =>
    {
        Console.WriteLine($"[-] {session.AccessId}");
    };
});

var app = builder.Build();

// gRPC 需要 HTTP/2
app.MapFeivooGamingServer();
app.Run();
```
