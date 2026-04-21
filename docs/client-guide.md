# 客户端接入指南

## 1. 安装

```xml
<PackageReference Include="Feivoo.Gaming.GrpcClient" Version="1.0.0" />
```

---

## 2. 实现消息处理器

平台会主动向客户端下发订单相关请求，**必须**实现 `IGamingMessageHandler` 的全部 4 个方法。
SDK 收到平台下发的指令后会自动调用对应方法，并将返回值回写给平台。
若方法抛出异常，SDK 会将异常信息包装为 `ResultFailure` 发送给平台，不会泄露堆栈细节。

```csharp
using Feivoo.Gaming.Grpc;
using Feivoo.Gaming.GrpcClient.Handlers;

public class MyMessageHandler : IGamingMessageHandler
{
    private readonly IOrderService _orderService;

    public MyMessageHandler(IOrderService orderService)
        => _orderService = orderService;

    /// <summary>平台通知客户端创建订单。</summary>
    public async Task<OrderSubmitResult> OnOrderSubmitAsync(OrderSubmit cmd, CancellationToken ct)
    {
        var order = await _orderService.SubmitAsync(cmd.GameId, cmd.Amount, ct);
        return new OrderSubmitResult { OrderId = order.Id };
    }

    /// <summary>平台通知客户端结算订单。</summary>
    public async Task<OrderSettleResult> OnOrderSettleAsync(OrderSettle cmd, CancellationToken ct)
    {
        await _orderService.SettleAsync(cmd.OrderId, ct);
        return new OrderSettleResult();
    }

    /// <summary>平台要求客户端撤单。</summary>
    public async Task<OrderRevokeResult> OnOrderRevokeAsync(OrderRevoke cmd, CancellationToken ct)
    {
        await _orderService.RevokeAsync(cmd.OrderId, ct);
        return new OrderRevokeResult();
    }

    /// <summary>平台查询客户端的待结算订单列表。</summary>
    public async Task<WaitingOrderListResult> OnWaitingOrderListAsync(WaitingOrderList cmd, CancellationToken ct)
    {
        var orders = await _orderService.GetWaitingAsync(ct);
        var result = new WaitingOrderListResult();
        result.Orders.AddRange(orders.Select(o => new OrderItem { OrderId = o.Id }));
        return result;
    }
}
```

---

## 3. 注册 DI（推荐）

```csharp
// Program.cs
builder.Services.AddFeivooGamingClient<MyMessageHandler>(options =>
{
    options.Address   = builder.Configuration["Gaming:Address"]!;
    options.AccessId  = builder.Configuration["Gaming:AccessId"]!;
    options.SecretKey = builder.Configuration["Gaming:SecretKey"]!;

    // 可选配置
    options.RequestTimeout    = TimeSpan.FromSeconds(10);
    options.DialTimeout       = TimeSpan.FromSeconds(10);
    options.AutoReconnect     = true;
    options.ReconnectDelay    = TimeSpan.FromMilliseconds(500);
    options.MaxReconnectDelay = TimeSpan.FromSeconds(10);

    options.OnStateChange = (state, ex) =>
    {
        Console.WriteLine($"[Gaming] 连接状态变更: {state}" + (ex is null ? "" : $" ({ex.Message})"));
    };
});
```

`GamingClient` 以单例注册，直接注入使用：

```csharp
public class MyService(GamingClient client) { }
```

### 无 DI 场景

```csharp
var client = new GamingClient(
    new GamingClientOptions
    {
        Address   = "https://gaming.example.com",
        AccessId  = "your-access-id",
        SecretKey = "your-secret-key",
    },
    new MyMessageHandler(orderService),
    loggerFactory.CreateLogger<GamingClient>());
```

---

## 4. 连接管理

首次调用 `RequestAsync` 或 `PushAsync` 时会自动建立连接，也可以主动连接：

```csharp
// 主动建立连接（可选，用于提前预热）
await client.ConnectAsync(cancellationToken);

// 等待连接就绪后再执行业务逻辑
await client.WaitUntilConnectedAsync(cancellationToken);
```

### 连接状态

```csharp
Console.WriteLine(client.State);       // ConnectionState 枚举
Console.WriteLine(client.IsConnected); // bool 快捷属性
Console.WriteLine(client.LastError);   // 最近一次导致断线的异常
```

`ConnectionState` 枚举值：

| 值 | 说明 |
|---|---|
| `Disconnected` | 未连接（初始状态或断线后） |
| `Connecting` | 正在建立连接 |
| `Connected` | 已连接，可正常收发 |
| `Reconnecting` | 断线后正在自动重连 |
| `Closed` | 已调用 `DisposeAsync`，永久关闭 |

---

## 5. 发起请求（客户端 → 平台）

### Request 模式（需要平台响应）

```csharp
// 查询频道
var channelResult = await client.RequestAsync(new ChannelCommand
{
    Query = new ChannelQuery { ChannelId = "ch-001" }
}, cancellationToken);

// 查询频道列表
var listResult = await client.RequestAsync(new ChannelCommand
{
    List = new ChannelList { /* 过滤条件 */ }
}, cancellationToken);

// 查询频道成员
var memberResult = await client.RequestAsync(new ChannelMemberCommand
{
    Query = new ChannelMemberQuery { ChannelId = "ch-001", PlayerId = "p-123" }
}, cancellationToken);

// 查询彩票游戏
var gameResult = await client.RequestAsync(new LotteryGameCommand
{
    Query = new LotteryGameQuery { GameId = "game-001" }
}, cancellationToken);

// 获取 Livekit Token
var livekitResult = await client.RequestAsync(new LivekitCommand
{
    TokenQuery = new LivekitTokenQuery { ChannelId = "ch-001" }
}, cancellationToken);
```

超时由 `GamingClientOptions.RequestTimeout` 控制（默认 10 秒）。

### Push 模式（单向，不等待响应）

```csharp
// 成员进入频道
await client.PushAsync(new ChannelMemberCommand
{
    Enter = new ChannelMemberEnter { ChannelId = "ch-001", PlayerId = "p-123" }
}, cancellationToken);

// 成员离开频道
await client.PushAsync(new ChannelMemberCommand
{
    Leave = new ChannelMemberLeave { ChannelId = "ch-001", PlayerId = "p-123" }
}, cancellationToken);
```

---

## 6. 订阅平台推送事件

以下事件为**平台主动下发**、无需客户端请求的信息性推送：

```csharp
// 平台推送的频道消息（聊天、系统通告等）
client.ChannelMessageReceived += async (msg, ct) =>
{
    Console.WriteLine($"收到频道消息: {msg.Content}");
    await Task.CompletedTask;
};

// 直播状态变更（开播、结束等）
client.LivekitStatusChanged += async (status, ct) =>
{
    Console.WriteLine($"直播状态: {status.IsLive}");
    await Task.CompletedTask;
};

// 期号 / 开奖号码更新
client.IssueUpdateReceived += async (update, ct) =>
{
    Console.WriteLine($"期号更新: {update.IssueNumber}");
    await Task.CompletedTask;
};
```

事件处理器抛出的异常会被 SDK 捕获并记录日志，不会中断读取循环。

---

## 7. 自动重连

`AutoReconnect = true`（默认）时，连接意外断开后 SDK 在后台自动重连：

```
首次断线 → 等 500ms → 重试
失败     → 等 1000ms → 重试
失败     → 等 2000ms → ...（上限 10s）
```

重连期间，`RequestAsync` 调用会触发 `EnsureConnectedAsync`，即：
- 如果 `_lifecycleCts` 还未取消，会重新建立连接后再发送请求
- 超时由 `DialTimeout` 控制

调用 `DisposeAsync()` 时，重连任务会被干净取消，不产生任何后台任务泄漏。

---

## 8. 优雅关闭

推荐通过 DI 生命周期管理（`IHostedService` + `IAsyncDisposable`），
或者在应用退出前手动调用：

```csharp
await client.DisposeAsync();
// 等价于
await client.CloseAsync();
```

`DisposeAsync` 会：
1. 取消所有后台任务（读循环 + 重连任务）
2. 等待后台任务干净退出
3. 完成 gRPC 请求流（`RequestStream.CompleteAsync`）
4. 关闭 gRPC Channel
5. 让所有挂起的 `RequestAsync` 调用抛出 `ObjectDisposedException`

---

## 9. 错误处理建议

```csharp
try
{
    var result = await client.RequestAsync(command, ct);
    if (result.Failure is not null)
    {
        // 平台返回的业务错误
        Console.WriteLine($"业务失败: {result.Failure.Code} - {result.Failure.Message}");
    }
}
catch (OperationCanceledException)
{
    // 超时或 CancellationToken 取消
}
catch (ObjectDisposedException)
{
    // 客户端已被 Dispose
}
catch (Exception ex)
{
    // gRPC 连接错误等
    Console.WriteLine($"请求失败: {ex.Message}");
}
```
