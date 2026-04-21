# 架构说明

## 包依赖关系

```
protos/protos/*.proto
        │
        ▼ (Grpc.Tools 编译，Access=Internal)
┌─────────────────────────────┐
│     Feivoo.Gaming.Grpc      │  netstandard2.0
│  ─ Protobuf 生成代码         │
│  ─ MessageIdGenerator       │
│  ─ RequestResponseCoordinator│
└────────────┬────────────────┘
             │ InternalsVisibleTo
     ┌───────┴────────┐
     ▼                ▼
┌──────────────┐  ┌──────────────────────────────┐
│ GrpcClient   │  │ GrpcServer.Abstractions       │
│  net10.0     │  │  net10.0                      │
│              │  │                               │
│ GamingClient │  │ MessageServiceImpl            │
│ IGaming      │  │ ClientMessageDispatcher       │
│  MessageHand-│  │ GamingSession                 │
│  ler         │  │ IGamingServerHandler          │
│ GamingClient │  │ IGamingSession                │
│  Options     │  │ IGamingServerAuthenticator    │
└──────────────┘  └──────────────────────────────┘
```

## Protobuf 封装策略

所有 `.proto` 文件在 `Feivoo.Gaming.Grpc` 中以 `Access="Internal"` 编译，
生成的 `ClientMessage`、`ServerMessage` 信封类型对外不可见。

消费者（`GrpcClient` 和 `GrpcServer.Abstractions`）通过 `InternalsVisibleTo` 访问信封，
但**不暴露**给业务方。业务方只操作类型化的 `*Command` / `*Result` 对象。

```
业务方代码
    │
    │ 只能看到 ChannelCommand、ChannelResult 等
    ▼
GamingClient / MessageServiceImpl   ← InternalsVisibleTo
    │
    │ 内部读写 ClientMessage / ServerMessage
    ▼
Feivoo.Gaming.Grpc（Internal）
```

这样做的好处：
- 业务方无法误构造信封，杜绝协议误用
- `message_id` 关联、`MessageMode` 标注完全由 SDK 内部保证
- 信封结构调整时，业务方代码无需修改

---

## 消息流向

### 客户端视角（GrpcClient）

```
业务方调用 RequestAsync(ChannelCommand)
    │
    ▼
EnsureConnectedAsync         ← 首次调用时建立 gRPC 双向流
    │
    ▼
RequestResponseCoordinator   ← 注册 message_id，返回 Task<ServerMessage>
    │
    ▼
WriteAsync → gRPC 请求流 ──────────────────────► 服务端
                                                      │
                               服务端处理后响应        │
ReadLoopAsync ◄── gRPC 响应流 ◄───────────────────────┘
    │
    ├─ TryComplete(message_id)  → RequestResponseCoordinator 完成等待
    │                              业务方 Task<ChannelResult> 返回
    │
    └─ DispatchAsync            → 无 message_id 匹配（平台主动推送）
            │
            ├─ LotteryOrder     → 调用 IGamingMessageHandler，WriteAsync 回执
            ├─ ChannelMessage   → 触发 ChannelMessageReceived 事件
            ├─ LivekitStatus    → 触发 LivekitStatusChanged 事件
            └─ IssueUpdate      → 触发 IssueUpdateReceived 事件
```

### 服务端视角（GrpcServer.Abstractions）

```
客户端建立 gRPC 双向流
    │
    ▼
MessageServiceImpl.Connect()
    │
    ├─ 读取 Metadata x-tenant-id / x-secret-key
    ├─ IGamingServerAuthenticator.AuthenticateAsync()
    │       └─ 返回 null → 拒绝连接（StatusCode.Unauthenticated）
    │       └─ 返回 GamingPrincipal → 建立 GamingSession
    │
    ▼
GamingSession 创建
    │
    ├─ GamingServerOptions.OnSessionConnected 回调
    │
    ▼
读循环（per 客户端消息）
    │
    ├─ Mode == Request → ClientMessageDispatcher.BuildReplyAsync()
    │       │
    │       ├─ PayloadCase 一级分发
    │       └─ ActionCase 二级分发 → IGamingServerHandler.On*Async()
    │                                       └─ 业务方返回 *Result
    │                                       └─ HandlerInvoker 包装异常为 ResultFailure
    │       └─ 构造 ServerMessage 写回客户端
    │
    └─ Mode == Push（客户端回执） → RequestResponseCoordinator.TryComplete()
            └─ IGamingSession.Request*Async 的等待 Task 完成
```

---

## 请求-响应关联机制

双向流中无法通过 HTTP/2 帧区分消息归属，所有关联通过 `message_id` 字段实现：

1. 发起方（客户端或服务端）生成唯一 `message_id`（`MessageIdGenerator`：UUID v4）
2. `RequestResponseCoordinator<TMessage>` 将 `message_id` → `TaskCompletionSource<TMessage>` 记录到字典
3. 发送消息，`Mode = Request`
4. 接收方处理后，以**相同** `message_id` 回复，`Mode = Push`
5. `ReadLoop` 收到回复时调用 `TryComplete(message_id)`，`TaskCompletionSource` 完成
6. 等待方 `await` 的 `Task<TMessage>` 返回
7. 超时由外层 `Task.WaitAsync(timeout)` 控制，超时后 `TaskCompletionSource` 被移除

---

## 自动重连状态机（GamingClient）

```
            ConnectAsync / RequestAsync / PushAsync
                    │
                    ▼
            ┌─────────────┐
            │ Disconnected│◄─────────────────────────────────────────┐
            └──────┬──────┘                                          │
                   │ EnsureConnectedAsync                            │ 重连失败（退避等待后重试）
                   ▼                                                  │
            ┌─────────────┐                                          │
            │  Connecting │                                          │
            └──────┬──────┘                                          │
           成功 /  │失败                                              │
               ┌───┘  └──► 抛出异常（调用方处理）                     │
               ▼                                                     │
        ┌───────────┐    ReadLoop 异常 / 流结束    ┌──────────────┐  │
        │ Connected │──────────────────────────►  │ Disconnected │──┘
        └───────────┘   （AutoReconnect=true）     └──────────────┘
               │
               │ DisposeAsync
               ▼
          ┌────────┐
          │ Closed │（不再重连）
          └────────┘
```

**重连细节：**
- 每次重连等待时间：`delay = min(delay × 2, MaxReconnectDelay)`
- 初始等待：`ReconnectDelay`（默认 500ms）
- 上限：`MaxReconnectDelay`（默认 10s）
- `DisposeAsync` 取消 `_lifecycleCts`，`Task.Delay` 立即退出，不泄露后台任务

---

## 目录结构约定

```
src/
├── Feivoo.Gaming.Grpc/
│   └── Feivoo/Gaming/Grpc/          ← namespace Feivoo.Gaming.Grpc.*
│       └── Messaging/
├── Feivoo.Gaming.GrpcClient/
│   ├── Feivoo/Gaming/GrpcClient/    ← namespace Feivoo.Gaming.GrpcClient.*
│   │   ├── Connection/
│   │   ├── Handlers/
│   │   └── Options/
│   └── Microsoft/Extensions/DependencyInjection/  ← DI 扩展
└── Feivoo.Gaming.GrpcServer.Abstractions/
    ├── Feivoo/Gaming/GrpcServer/    ← namespace Feivoo.Gaming.GrpcServer.*
    │   └── Abstractions/            ← namespace Feivoo.Gaming.GrpcServer.Abstractions
    ├── Microsoft/Extensions/DependencyInjection/  ← DI 扩展
    └── Microsoft/AspNetCore/Builder/              ← 路由扩展
```

所有项目均设置 `<RootNamespace></RootNamespace>`，命名空间完全由物理路径决定，
不依赖 MSBuild 自动推导。
