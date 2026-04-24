# 变更日志

本文件记录所有重要版本变更，格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [未发布]

## [2.0.1] - 2026-04-24

### Fixed

- **`GamingSessionTracker` DI 构造失败**：v2.0.0 把 `ITransport` 当作 keyed service 注入（`[FromKeyedServices(GamingMessageChannel.Name)] ITransport`），但 Vertex 1.0.1 的 `AddGrpcServerTransport` 实际上是把 `ITransport` 注册为**普通 singleton** + 在 `ITransportRegistry` 里按名字注册。导致任何 `AddFeivooGamingServer<THandler, TAuth>()` 的宿主 `ServiceProvider` 构造时抛 `InvalidOperationException: Unable to resolve service for type 'Vertex.Transport.ITransport'`。改为通过 `ITransportRegistry.Get(GamingMessageChannel.Name)` 解析，与 Vertex DI 约定一致。

## [2.0.0] - 2026-04-24

基于 Vertex.Messaging + Vertex.Transport.Grpc 的 SDK 重写。wire 协议从 oneof ClientMessage/ServerMessage 切换为 Vertex 4-frame envelope，并与 `feivoo-gaming-go-sdk 2.0.0` / `feivoo-gaming 2.0.0` 服务端配套。

### Breaking changes

- **`Feivoo.Gaming.GrpcServer.Abstractions`**
  - 移除 `IGamingServerHandler`、`WalletServiceImpl`、`ClientMessageDispatcher`、`HandlerInvoker`、`MessageIdGenerator`、`RequestResponseCoordinator`、具体 `GamingSession`、`AuthHeaders`；改为 19 个强类型 `Vertex.Messaging.IRpcHandler<TReq, TResp>`（`MerchantOnlineNotify` / `OrderCreate` / `OrderSettle` / `OrderCancel` / `OrderSubmit` / `OrderRevoke` / `WalletBalanceQuery` / `WalletAllBalancesQuery` / `WalletBalanceAdd` / `WalletBalanceSubtract` / `WalletTransfer` / `WalletFreeze` / `WalletUnfreeze` / `WalletFreezeWithTransfer` / `WalletUnfreezeWithTransfer` / `WalletFrozenQuery` / `ChannelMessageSubmit` / `WalletBalanceChangedNotify` / `LotteryOrderCancelingHandle`），请求/响应类型直接使用 `Feivoo.Gaming.Grpc.Proto` 下的 proto-generated 类型。
  - DI 入口：`AddFeivooGamingServer<THandler, TAuth>()` + `MapFeivooGamingServer()`，内部等价于 `AddGrpcServerTransport` + `AddMessagingChannel("feivoo-gaming-message")` + 19× `AddRpcHandler<TReq, TResp, THandler>`。
  - `IGamingSession` 改为 `VertexGamingSession`：基于 `IMessageBus.PublishAsync` + `IRpcClient.InvokeAsync` 按 `PeerId`（= `x-tenant-id`）寻址，不再暴露具体 stream writer。
  - 身份校验迁移到 `GamingAuthInterceptor`（`x-tenant-id` / `x-secret-key` metadata + `x-vertex-peer-id=tenant-id`）。
  - 连接生命周期迁移到 `GamingSessionTracker`（`IHostedService`），监听 `PeerConnectionChanged` 事件触发 `OnSessionConnected` / `OnSessionDisconnected` 回调。

- **`Feivoo.Gaming.GrpcClient`**
  - `GamingClient` 重写为 Vertex 消息通道客户端；19 个 `RequestAsync` 改为 `InvokeAsync<TReq, TResp>` 语义，请求/响应全部使用 `Feivoo.Gaming.Grpc.Proto` 下的 proto-generated 类型（v1.3.0 的手写 DTO 已移除）。
  - **19 个 `PushAsync*` 方法被移除**（v1.3.0 的 fire-and-forget 单向通知在 Vertex envelope 下不再需要；所有客户端→服务端语义统一为 `Invoke` 请求响应）。
  - 12 个服务端事件改为 `Subscribe<T>` 模式扇出到每事件回调；5 个服务端反向 RPC（订单下单 / 结算 / 取消 / 订单 Revoke / 钱包查询）改为 Vertex `IRpcHandler<T>` 适配器。
  - 结构化错误码：服务端抛出的 `GamingRemoteException` 以 `"{CodeName}|{UserMessage}"` 形式编码在 gRPC trailer，客户端解码为 `GamingRemoteException.ErrorCode` + `UserMessage` 两个属性（v1.3.0 仅暴露纯文本消息）。

### 迁移要点

- 服务端实现从 `IGamingServerHandler` 迁移到 19 个 `IRpcHandler<>`：可以用一个 `sealed class` 显式实现 19 个接口；原来的 switch-case 逻辑拆成每个接口一个 `HandleAsync`。
- 服务端侧获取调用方身份：不再从 `session.AccessId` 取，改为 `ctx.From.Value`（Vertex `PeerId`，值即 tenant-id）。
- 客户端 `PushAsync*` 调用需要改写为对应 `RequestAsync*`。确有必要的 fire-and-forget 可自行 `Task.Run(() => client.RequestAsync...)` 包装，但建议检查是否真的不需要 ack。

### 依赖

- 新增：`Vertex.Messaging` 1.0.1、`Vertex.Transport.Grpc` 1.0.1。
- Grpc.Net.Client 升到 2.71.0，Microsoft.Extensions.* 升到 10.0.0。

## [1.3.0] - 2026-04-23

修复 bidi 流在「调用方超时取消写操作」时整条会话被打挂的协议层缺陷。

### 修复（**关键 / 影响线上稳定性**）

- **`GamingSession.SendAsync` 不再把 `cancellationToken` 传给 `IServerStreamWriter.WriteAsync`。**
  旧实现一旦 CT 在写入过程中取消（最常见的触发：调用方对 reverse-direction 请求设置了超时，
  超时到点 cancel；或客户端正好断线让 `context.CancellationToken` 翻转），gRPC 会向底层
  HTTP/2 stream 发送 RST_STREAM，**整条 bidi 会话立即报废**：
  - 同一连接上其他正在等待 Ack 的 RPC（订单结算、钱包查询、消息提交等）全部拿到
    `IOException` / `RpcException`，被迫超时；
  - merchant 侧表现为：一次慢回调 → 这条调用超时 → 之后**所有**业务都收不到任何回复，
    整个通信完全中断，必须重连才能恢复。
- 新实现：`SendAsync` 仅用 CT 等待 `_writeLock`；一旦拿到锁，必须把整条消息无中断地
  落到 wire 上。流级别的真正终止由读循环统一负责检测和上报。

### 兼容性

- 协议无改动；API 签名无变化。
- 行为变化仅在「写入过程中调用方取消 CT」这种异常路径出现：旧实现会**毁掉整条流**，
  新实现会**完成本次写**然后让 CT 在下一次有意义的等待点（接收方处理完毕后调用方下一次
  操作）抛出 `OperationCanceledException`。这与 gRPC 一贯的「不要在 WriteAsync 中途
  取消」契约一致。

### 备注

- 与同日发布的 `gaming-go-sdk 1.3.0` 配套：Go SDK 同时修复了「单条 reply 发送失败
  误把整条流拆掉」的对称缺陷，并把 reverse-direction handler 改为并发派发。

## [1.2.0] - 2026-04-22

基于 `gaming-protos` 1.2.0：为 `ChannelMessageSubmit` 同步响应补全强类型业务拒绝通道。

### 新增

- `ResultErrorCode.RuleViolation` (2004)：业务规则拒绝（限额、对冲、风控、未配置玩法等）。
- `ChannelMessageHandled.Rejection` (字段号 3，类型 `ResultFailure`)：拒绝时**只**填该字段，`Intent` 留 `Unknown`、`Data` 留空。

### 兼容性

- 协议层完全向后兼容（仅新增字段号 3、新增枚举值 2004）。
- 老版本商户 SDK 解 Ack 时忽略 `rejection`，仍可消费 `intent`/`data`；过渡期内服务端可同时填两份。
- 推荐迁移：商户 SDK 优先判断 `Handled.Rejection != null`，再消费 `Intent`/`Data`。

## [1.1.0] - 2026-04-22

基于 `gaming-protos` 1.1.0：把返水/积分领域拆分为独立的 `Cashback` 与 `PointAward`，并在结算流程中真正填充 `OrderSettlement.Rewards`。

### 变更

- `OrderSettlement` 新增 `Rewards` 字段（`Points[]` + `Cashbacks[]`）。
- 单笔 `OrderSettle` 推送与整期 `IssueFinished` 公告**都会**携带真实奖励数据；服务端在结算阶段一次性算好并随事件透传，**与商户在线状态无关**——商户离线/单笔 push 失败也不会影响公告里的奖励完整性。
- `ClaimAck` 系列响应（领取返水/全部领取）改为返回 `Rewards`。

### 移除

- 删除 `Rebate` / `RebateType`（被 `Cashback` + `PointAward` 替代）。商户消费方需要把旧字段读取改成读 `Rewards.Points` / `Rewards.Cashbacks`。


第一个面向生产的正式发布版本，基于扁平 envelope 架构（`gaming-protos` v1.0.0）。

### 新增

#### 包结构

- `Feivoo.Gaming.Grpc` (netstandard2.0)：共享运行时，包含 Protobuf 生成代码、`MessageIdGenerator`、
  `RequestResponseCoordinator`；通过 `InternalsVisibleTo` 向消费者暴露内部实现
- `Feivoo.Gaming.GrpcClient` (net10.0)：客户端 SDK
- `Feivoo.Gaming.GrpcServer.Abstractions` (net10.0)：服务端 SDK

#### 客户端（`Feivoo.Gaming.GrpcClient`）

- `GamingClient`：gRPC 双向流客户端（扁平 envelope 架构）
  - 19 个 `RequestAsync` 重载，覆盖全部消息类型（Channel×5、ChannelMessage×1、ChannelMember×8、
    WaitingOrderListQuery×1、LotteryGame×2、GameNode×1、Livekit×1）
  - 19 个 `PushAsync` 重载（对应同上，单向，不等待响应）
  - `ConnectAsync` / `WaitUntilConnectedAsync`
  - 自动重连（指数退避：`ReconnectDelay` → `MaxReconnectDelay`，受 `DialTimeout` 约束）
  - `ConnectionState` 生命周期枚举 + `OnStateChange` 状态变更回调
  - 期号事件：`IssueCountdownReceived`、`IssueOpeningReceived`、`IssueStoppingReceived`、
    `IssueDrawingReceived`、`IssueFinishedReceived`、`IssueCheckoutReceived`、`IssueTerminatedReceived`
  - 直播事件：`LivekitLiveChangedReceived`、`LivekitRoomStartedReceived`、`LivekitRoomFinishedReceived`、
    `LivekitTrackPublishedReceived`、`LivekitTrackUnpublishedReceived`
- `IGamingMessageHandler`：客户端必须实现的 5 个业务回调方法
  - `OnOrderSubmitAsync`、`OnOrderSettleAsync`、`OnOrderRevokeAsync`
  - `OnChannelMemberWalletQueryAsync`、`OnChannelMemberAllWalletsQueryAsync`
- `GamingClientOptions`：完整配置项，含 XML 注释
- DI 扩展：`AddFeivooGamingClient<THandler>(services, configure)`

#### 服务端（`Feivoo.Gaming.GrpcServer.Abstractions`）

- `IGamingServerHandler`：22 个子操作处理方法
  - Channel：Create / Update / Query / List / Delete
  - ChannelMessage：Submit
  - ChannelMember：Enter / Leave / Query / Lookup / TurnoverQuery / RecentOrderQuery / RebateClaim / AllRebatesClaim
  - WaitingOrderListQuery（客户端→平台）
  - LotteryGame：Query / List
  - GameNode：Query
  - Livekit：TokenQuery
- `IGamingSession`：单连接会话句柄
  - 期号推送：`PushIssueCountdownAsync`、`PushIssueOpeningAsync`、`PushIssueStoppingAsync`、
    `PushIssueDrawingAsync`、`PushIssueFinishedAsync`、`PushIssueCheckoutAsync`、`PushIssueTerminatedAsync`
  - 直播推送：`PushLivekitLiveChangedAsync`、`PushLivekitRoomStartedAsync`、`PushLivekitRoomFinishedAsync`、
    `PushLivekitTrackPublishedAsync`、`PushLivekitTrackUnpublishedAsync`
  - 平台→客户端请求：`RequestOrderSubmitAsync`、`RequestOrderSettleAsync`、`RequestOrderRevokeAsync`
  - 钱包查询：`RequestChannelMemberWalletQueryAsync`、`RequestChannelMemberAllWalletsQueryAsync`
- `IGamingServerAuthenticator` + `GamingPrincipal`：基于 gRPC Metadata 的认证体系
- `ClientMessageDispatcher`：扁平 switch 分发全部操作到对应处理方法
- `MessageServiceImpl`：内部 `MessageService.MessageServiceBase` gRPC 服务实现
- `GamingServerOptions`：`RequestTimeout`、`OnSessionConnected`、`OnSessionDisconnected`
- DI 扩展：`AddFeivooGamingServer<THandler, TAuthenticator>(services, configure?)`
- 路由扩展：`MapFeivooGamingServer(endpoints)`（命名空间 `Microsoft.AspNetCore.Builder`）

#### 工程规范

- 严格 folder = namespace 约定；所有项目 `<RootNamespace>` 为空
- Protobuf 以 `Access="Internal"` 编译；消费者通过 `InternalsVisibleTo` 访问
- DI 扩展置于 `Microsoft.Extensions.DependencyInjection` 命名空间
- 路由扩展置于 `Microsoft.AspNetCore.Builder` 命名空间
- NuGet 元数据：`Authors`、`PackageTags`、`PackageLicenseExpression=MIT`、`RepositoryUrl`
- README 内嵌到 NuGet 包（`PackageReadmeFile`）
- 所有公开包生成 XML 文档文件（`GenerateDocumentationFile=true`）

