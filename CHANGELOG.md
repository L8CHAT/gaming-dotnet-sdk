# 变更日志

本文件记录所有重要版本变更，格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [未发布]

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

