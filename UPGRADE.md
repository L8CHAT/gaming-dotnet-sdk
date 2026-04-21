# 升级指南

## v1.x → v1.0.0（正式版）

v2 是完整重写，v1.x 的 API 不再保留。以下是主要迁移点。

---

### 客户端迁移

#### 1. 改用 DI 注册

旧（v1.x）：手动构造 `GamingClient`，直接传入参数。

新（v1.0.0）：通过 DI + Options 模式注册：

```csharp
// Program.cs 或 Startup.cs
builder.Services.AddFeivooGamingClient<MyMessageHandler>(options =>
{
    options.Address   = "https://gaming.example.com";
    options.AccessId  = "your-access-id";
    options.SecretKey = "your-secret-key";
});
```

#### 2. 必须实现 IGamingMessageHandler

v1.x 中平台无法主动向客户端发起请求；v1.0.0 中平台会主动下发订单相关请求，
必须实现 `IGamingMessageHandler` 的全部 4 个方法：

```csharp
public class MyMessageHandler : IGamingMessageHandler
{
    // 平台要求客户端创建订单
    public Task<OrderSubmitResult> OnOrderSubmitAsync(OrderSubmit cmd, CancellationToken ct)
        => Task.FromResult(new OrderSubmitResult { /* 填充业务结果 */ });

    // 平台通知订单结算
    public Task<OrderSettleResult> OnOrderSettleAsync(OrderSettle cmd, CancellationToken ct)
        => Task.FromResult(new OrderSettleResult { /* ... */ });

    // 平台要求撤单
    public Task<OrderRevokeResult> OnOrderRevokeAsync(OrderRevoke cmd, CancellationToken ct)
        => Task.FromResult(new OrderRevokeResult { /* ... */ });

    // 平台查询待结算订单列表
    public Task<WaitingOrderListResult> OnWaitingOrderListAsync(WaitingOrderList cmd, CancellationToken ct)
        => Task.FromResult(new WaitingOrderListResult { /* ... */ });
}
```

#### 3. 发送请求改用类型化重载

旧（v1.x）：直接构造 Protobuf 信封（`ClientMessage`）并发送。

新（v1.0.0）：通过类型化重载，信封由 SDK 内部构造：

```csharp
// Request（需要平台响应）
var result = await client.RequestAsync(new ChannelCommand
{
    Query = new ChannelQuery { ChannelId = "ch-001" }
}, cancellationToken);

// Push（单向，不等待响应）
await client.PushAsync(new ChannelMemberCommand
{
    Enter = new ChannelMemberEnter { ChannelId = "ch-001", PlayerId = "p-123" }
}, cancellationToken);
```

#### 4. 订阅平台推送事件

```csharp
client.ChannelMessageReceived += (msg, ct) => { /* 处理频道消息 */ return Task.CompletedTask; };
client.LivekitStatusChanged   += (status, ct) => Task.CompletedTask;
client.IssueUpdateReceived    += (update, ct) => Task.CompletedTask;
```

#### 5. 自动重连（新功能）

默认开启，无需额外配置。如需调整：

```csharp
options.AutoReconnect    = true;           // 默认 true
options.ReconnectDelay   = TimeSpan.FromMilliseconds(500); // 首次等待
options.MaxReconnectDelay = TimeSpan.FromSeconds(10);      // 退避上限
options.DialTimeout      = TimeSpan.FromSeconds(10);       // 单次连接超时
```

---

### 服务端迁移

v1.x 没有对应的服务端 SDK，v1.0.0 为全新功能，直接按以下步骤接入：

1. 引用 `Feivoo.Gaming.GrpcServer.Abstractions`
2. 实现 `IGamingServerAuthenticator`（校验 `x-tenant-id` + `x-secret-key`）
3. 实现 `IGamingServerHandler`（20 个子操作方法）
4. 注册 DI：`AddFeivooGamingServer<THandler, TAuthenticator>`
5. 挂载路由：`app.MapFeivooGamingServer()`

详见 [docs/server-guide.md](docs/server-guide.md)。

---

### 配置类变更

`GamingClientOptions` 属性由 `required init` 改为普通 `set`，
两种构造方式均支持：

```csharp
// 方式 A：DI + Options 模式（推荐）
services.AddFeivooGamingClient<MyHandler>(opt => { opt.Address = "..."; });

// 方式 B：直接构造（适用于无 DI 场景）
var client = new GamingClient(
    new GamingClientOptions { Address = "...", AccessId = "...", SecretKey = "..." },
    new MyMessageHandler());
```
