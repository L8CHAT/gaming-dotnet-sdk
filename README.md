# Feivoo Gaming .NET SDK

Feivoo Gaming 平台的 .NET SDK，提供 gRPC 双向流通信的客户端和服务端集成能力。

## 文档

- [架构说明](docs/architecture.md) — 包依赖、消息流向、请求-响应关联、自动重连状态机
- [客户端接入指南](docs/client-guide.md) — 详细的客户端使用说明
- [服务端接入指南](docs/server-guide.md) — 详细的服务端使用说明
- [兼容性说明](COMPATIBILITY.md) — SDK 与协议版本对照、依赖版本
- [升级指南](UPGRADE.md) — 从旧版本迁移
- [变更日志](CHANGELOG.md)

## 包结构

| 包 | 目标框架 | 说明 |
|---|---|---|
| `Feivoo.Gaming.Grpc` | netstandard2.0 | 共享 Protobuf 生成代码 + 运行时工具（内部包，通常无需直接引用） |
| `Feivoo.Gaming.GrpcClient` | net10.0 | 客户端 SDK |
| `Feivoo.Gaming.GrpcServer.Abstractions` | net10.0 | 服务端 SDK |

---

## 客户端（GrpcClient）

### 安装

```xml
<PackageReference Include="Feivoo.Gaming.GrpcClient" Version="*" />
```

### 快速上手（Generic Host / ASP.NET Core）

**1. 实现消息处理器**

平台会主动向客户端下发订单相关请求，客户端必须实现 `IGamingMessageHandler` 来响应：

```csharp
using Feivoo.Gaming.GrpcClient.Handlers;
using Feivoo.Gaming.Grpc;

public class MyMessageHandler : IGamingMessageHandler
{
    public Task<OrderSubmitResult> OnOrderSubmitAsync(OrderSubmit command, CancellationToken ct)
        => Task.FromResult(new OrderSubmitResult { /* ... */ });

    public Task<OrderSettleResult> OnOrderSettleAsync(OrderSettle command, CancellationToken ct)
        => Task.FromResult(new OrderSettleResult { /* ... */ });

    public Task<OrderRevokeResult> OnOrderRevokeAsync(OrderRevoke command, CancellationToken ct)
        => Task.FromResult(new OrderRevokeResult { /* ... */ });

    public Task<WaitingOrderListResult> OnWaitingOrderListAsync(WaitingOrderList command, CancellationToken ct)
        => Task.FromResult(new WaitingOrderListResult { /* ... */ });
}
```

**2. 注册 DI**

```csharp
builder.Services.AddFeivooGamingClient<MyMessageHandler>(options =>
{
    options.Address   = "https://gaming.example.com";
    options.AccessId  = "your-access-id";
    options.SecretKey = "your-secret-key";
});
```

**3. 使用客户端**

```csharp
public class MyService(GamingClient client)
{
    public async Task RunAsync(CancellationToken ct)
    {
        // 可选：主动连接（首次 RequestAsync/PushAsync 时也会自动连接）
        await client.ConnectAsync(ct);

        // 订阅平台推送事件（可选）
        client.ChannelMessageReceived += (msg, token) => { /* ... */ return Task.CompletedTask; };
        client.LivekitStatusChanged   += (status, token) => Task.CompletedTask;
        client.IssueUpdateReceived    += (update, token) => Task.CompletedTask;

        // 发起请求
        var result = await client.RequestAsync(new ChannelCommand
        {
            Query = new ChannelQuery { ChannelId = "ch-001" }
        }, ct);

        // 单向推送
        await client.PushAsync(new ChannelMemberCommand
        {
            Enter = new ChannelMemberEnter { ChannelId = "ch-001", PlayerId = "p-123" }
        }, ct);
    }
}
```

### GamingClientOptions

| 属性 | 默认值 | 说明 |
|---|---|---|
| `Address` | `""` | gRPC 服务地址（`https://...`） |
| `AccessId` | `""` | 认证用 AccessId，写入 `x-tenant-id` header |
| `SecretKey` | `""` | 认证密钥，写入 `x-secret-key` header |
| `RequestTimeout` | `10s` | 单次请求等待响应的超时 |
| `DialTimeout` | `10s` | 建立连接的超时（含首次连接和重连） |
| `AutoReconnect` | `true` | 连接断开后是否自动重连 |
| `ReconnectDelay` | `500ms` | 首次重连前的等待时间 |
| `MaxReconnectDelay` | `10s` | 指数退避上限 |
| `OnStateChange` | `null` | 连接状态变更回调 `(ConnectionState, Exception?) => void` |

### 自动重连

当 `AutoReconnect = true`（默认）时，连接意外断开后 SDK 会自动以指数退避策略重连：

```
首次断开 → 等待 ReconnectDelay(500ms) → 重试
再次失败 → 等待 1000ms → 重试
再次失败 → 等待 2000ms → ...（上限 MaxReconnectDelay）
```

调用 `DisposeAsync()` / `CloseAsync()` 时，重连任务会被干净取消，不会泄露后台任务。

### Request vs Push

| 方法 | 语义 |
|---|---|
| `RequestAsync(command, ct)` | 发送请求并等待平台响应，超时由 `RequestTimeout` 控制 |
| `PushAsync(command, ct)` | 单向发送，不等待响应 |

支持的 Command 类型：`ChannelCommand`、`ChannelMemberCommand`、`ChannelMessageCommand`、`LotteryGameCommand`、`GameNodeCommand`、`LivekitCommand`

---

## 服务端（GrpcServer.Abstractions）

### 安装

```xml
<PackageReference Include="Feivoo.Gaming.GrpcServer.Abstractions" Version="*" />
```

### 快速上手（ASP.NET Core）

**1. 实现认证器**

```csharp
using Feivoo.Gaming.GrpcServer.Abstractions;
using Feivoo.Gaming.GrpcServer;

public class MyAuthenticator : IGamingServerAuthenticator
{
    public Task<GamingPrincipal?> AuthenticateAsync(string accessId, string secretKey, CancellationToken ct)
    {
        // 校验 accessId + secretKey，不通过返回 null（连接将被拒绝）
        if (secretKey != "expected") return Task.FromResult<GamingPrincipal?>(null);
        return Task.FromResult<GamingPrincipal?>(new GamingPrincipal { AccessId = accessId });
    }
}
```

**2. 实现业务处理器**

```csharp
using Feivoo.Gaming.GrpcServer.Abstractions;
using Feivoo.Gaming.Grpc;

public class MyServerHandler : IGamingServerHandler
{
    public Task<ChannelCreateResult> OnChannelCreateAsync(ChannelCreate cmd, IGamingSession session, CancellationToken ct)
        => Task.FromResult(new ChannelCreateResult { /* ... */ });

    // ... 其余 19 个方法
}
```

**3. 注册 DI 并挂载 endpoint**

```csharp
// Program.cs
builder.Services.AddFeivooGamingServer<MyServerHandler, MyAuthenticator>(options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(15);
    options.OnSessionConnected    = session => Console.WriteLine($"+ {session.AccessId}");
    options.OnSessionDisconnected = (session, ex) => Console.WriteLine($"- {session.AccessId}");
});

var app = builder.Build();
app.MapFeivooGamingServer();
app.Run();
```

### IGamingServerHandler 方法一览

#### Channel（频道）

| 方法 | 触发时机 |
|---|---|
| `OnChannelCreateAsync` | 客户端创建频道 |
| `OnChannelUpdateAsync` | 客户端更新频道 |
| `OnChannelQueryAsync` | 客户端查询单个频道 |
| `OnChannelListAsync` | 客户端获取频道列表 |
| `OnChannelDeleteAsync` | 客户端删除频道 |

#### ChannelMember（频道成员）

| 方法 | 触发时机 |
|---|---|
| `OnChannelMemberEnterAsync` | 成员进入频道 |
| `OnChannelMemberLeaveAsync` | 成员离开频道 |
| `OnChannelMemberQueryAsync` | 查询成员信息 |
| `OnChannelMemberLookupAsync` | 精确查找成员 |
| `OnChannelMemberWalletQueryAsync` | 查询单个钱包 |
| `OnChannelMemberAllWalletsQueryAsync` | 查询所有钱包 |
| `OnChannelMemberTurnoverQueryAsync` | 查询流水 |
| `OnChannelMemberRecentOrderQueryAsync` | 查询最近订单 |
| `OnChannelMemberRebateClaimAsync` | 单笔返水申领 |
| `OnChannelMemberAllRebatesClaimAsync` | 批量返水申领 |

#### 其他

| 方法 | 触发时机 |
|---|---|
| `OnChannelMessageSubmitAsync` | 客户端发送频道消息 |
| `OnLotteryGameQueryAsync` | 查询单个彩票游戏 |
| `OnLotteryGameListAsync` | 获取彩票游戏列表 |
| `OnGameNodeQueryAsync` | 查询游戏节点信息 |
| `OnLivekitTokenQueryAsync` | 获取 Livekit Token |

### IGamingSession — 服务端主动下行

每个 handler 方法都接收 `IGamingSession session`，通过它可以向该客户端推送消息或发起请求：

```csharp
// 推送（无需等待客户端确认）
await session.PushChannelMessageAsync(channelMsg, ct);
await session.PushLivekitStatusAsync(status, ct);
await session.PushIssueUpdateAsync(update, ct);

// 请求（等待客户端回执，超时由 GamingServerOptions.RequestTimeout 控制）
var submitResult = await session.RequestOrderSubmitAsync(new OrderSubmit { /* ... */ }, ct);
var settleResult = await session.RequestOrderSettleAsync(new OrderSettle { /* ... */ }, ct);
var revokeResult = await session.RequestOrderRevokeAsync(new OrderRevoke { /* ... */ }, ct);
var listResult   = await session.RequestWaitingOrderListAsync(new WaitingOrderList { /* ... */ }, ct);
```

---

## 通信协议

- 传输：gRPC 双向流（`MessageService.Connect`）
- 认证：gRPC Metadata — `x-tenant-id` (AccessId) + `x-secret-key`
- 消息模式：`MessageMode.Request`（需响应）/ `MessageMode.Push`（单向）
- 关联：`message_id` 字段用于请求-响应配对