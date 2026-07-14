# 变更日志

本文件记录所有重要版本变更，格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

工作流（参考 [组织级 CLAUDE.md §7](../CLAUDE.md)）：每个 PR 合 main 时作者在 `[Unreleased]` 段追加条目并带 PR 链接 `[#N](url)`；打 stable tag `vX.Y.Z` 时把本节标题改成 `[vX.Y.Z] - YYYY-MM-DD`，再开一个空的 `[Unreleased]`。**prerelease tag (`-rc.N` / `-beta.N`) 不开新节**。

滚动 prerelease 包（`{X.Y.Z}-main.{run_number}.{sha}`）由 `.github/workflows/release.yml` 在 `push: branches: [main]` 时自动产出，**不打 git tag**。下游消费方用 floating range 引用。

## [Unreleased]

### Changed

- protos submodule → main（[gaming-protos#10](https://github.com/L8CHAT/gaming-protos/pull/10)）：`ChannelConfig.betting_mode` + `BettingMode` 枚举 [#11](https://github.com/L8CHAT/gaming-dotnet-sdk/pull/11)

## [v1.0.0] - 2026-05-02

Gaming .NET SDK 首个正式 release，对接 [gaming-protos v1.0.0](https://github.com/L8CHAT/gaming-protos/releases/tag/v1.0.0) + `gaming-go-sdk v1.0.0` + `Feivoo.Gaming` 服务端。

### Added

- **包结构**：
  - `Feivoo.Gaming.Grpc` (netstandard2.0)：共享运行时，含 protobuf 生成代码 + `MessageIdGenerator` + `RequestResponseCoordinator`；通过 `InternalsVisibleTo` 暴露内部实现给消费包
  - `Feivoo.Gaming.GrpcClient` (net10.0)：客户端 SDK
  - `Feivoo.Gaming.GrpcServer.Abstractions` (net10.0)：服务端 SDK

- **传输层**：基于 `Vertex.Messaging` + `Vertex.Transport.Grpc` 的 4-frame envelope 协议。
  - 客户端：`GamingClient` 通过 Vertex 消息通道连接服务端，`InvokeAsync<TReq, TResp>` 语义请求响应；自动重连（指数退避 `ReconnectDelay` → `MaxReconnectDelay`，受 `DialTimeout` 约束）；`ConnectionState` 生命周期枚举 + `OnStateChange` 状态变更回调。
  - 服务端：`AddFeivooGamingServer<THandler, TAuth>()` + `MapFeivooGamingServer()` DI 入口，内部等价于 `AddGrpcServerTransport` + `AddMessagingChannel("feivoo-gaming-message")` + 多个 `AddRpcHandler<TReq, TResp, THandler>`。
  - 身份校验：`GamingAuthInterceptor`（`x-tenant-id` / `x-secret-key` metadata + `x-vertex-peer-id=tenant-id`）。
  - 连接生命周期：`GamingSessionTracker`（`IHostedService`），监听 `PeerConnectionChanged` 事件触发 `OnSessionConnected` / `OnSessionDisconnected` 回调。
  - 服务端通过 `ITransportRegistry.Get(GamingMessageChannel.Name)` 解析 `ITransport`（与 Vertex DI 约定一致）。

- **强类型 RPC handler**：服务端 22 个 `IRpcHandler<TReq, TResp>` 接口（`MerchantOnlineNotify` / `OrderCreate` / `OrderSettle` / `OrderCancel` / `OrderSubmit` / `OrderRevoke` / `WalletBalanceQuery` / `WalletAllBalancesQuery` / `WalletBalanceAdd` / `WalletBalanceSubtract` / `WalletTransfer` / `WalletFreeze` / `WalletUnfreeze` / `WalletFreezeWithTransfer` / `WalletUnfreezeWithTransfer` / `WalletFrozenQuery` / `ChannelMessageSubmit` / `WalletBalanceChangedNotify` / `LotteryOrderCancelingHandle` / `ChannelConfigQuery` / `ChannelMemberPointBalanceQuery` / `ChannelMemberCashbackBalanceQuery`），请求/响应类型直接使用 `Feivoo.Gaming.Grpc.Proto` 下的 proto-generated 类型。

- **客户端 RPC 调用**：`GamingClient.InvokeAsync<TReq, TResp>(...)` 覆盖全部消息类型：频道 CRUD（5）/ 频道整合配置（1）/ 成员积分余额（1）/ 成员返水余额（1）/ 频道成员（4）/ 成员流水（1）/ 成员订单（1）/ 成员返佣（2）/ 频道消息（1）/ 待开奖订单（1）/ 彩种（2）/ 游戏节点（1）/ Livekit（1）。

- **服务端事件订阅**（12 个 `Subscribe<T>` 模式扇出到每事件回调）：
  - 期号生命周期：`IssueCountdownReceived` / `IssueOpeningReceived` / `IssueStoppingReceived` / `IssueDrawingReceived` / `IssueFinishedReceived` / `IssueCheckoutReceived` / `IssueTerminatedReceived`
  - Livekit 状态：`LivekitLiveChangedReceived` / `LivekitRoomStartedReceived` / `LivekitRoomFinishedReceived` / `LivekitTrackPublishedReceived` / `LivekitTrackUnpublishedReceived`

- **服务端反向 RPC**（5 个 `IRpcHandler<T>` 适配器）：订单下单 / 结算 / 取消 / 订单 Revoke / 钱包查询。

- **`IGamingMessageHandler`**：客户端实现的 5 个业务回调方法 `OnOrderSubmitAsync` / `OnOrderSettleAsync` / `OnOrderRevokeAsync` / `OnChannelMemberWalletQueryAsync` / `OnChannelMemberAllWalletsQueryAsync`。

- **结构化错误码**：服务端抛出的 `GamingRemoteException` 以 `"{CodeName}|{UserMessage}"` 形式编码在 gRPC trailer，客户端解码为 `GamingRemoteException.ErrorCode` + `UserMessage` 两个属性。错误码包括 17 个标准 + `RateLimited` (1009) / `Unavailable` (1010) / `QuotaExhausted` (3004) / `AccountFrozen` (4002) / `RuleViolation` (2004)。

- **In-band 业务拒绝统一**：服务端通过 `ChannelMessageHandled.Rejection` (字段号 3，类型 `ResultFailure`) 返回的业务拒绝（封盘、规则违规、余额不足等），客户端不再需要检查响应体字段——SDK 直接抛出和 gRPC trailer 同一个 `GamingRemoteException`。调用方 `try catch GamingRemoteException`，用 `ErrorCode` 分流即可。

- **`ResultFailure.Details`**（`google.protobuf.Any`）：结构化附加信息，按 code 决定反序列化类型；未识别 details 应忽略。

- **结算奖励**：`OrderSettlement` 的 `Rewards` 字段（`Points[]` + `Cashbacks[]`）。单笔 `OrderSettle` 推送与整期 `IssueFinished` 公告**都会**携带真实奖励数据；服务端在结算阶段一次性算好并随事件透传，**与商户在线状态无关**——商户离线/单笔 push 失败也不会影响公告里的奖励完整性。`ClaimAck` 系列响应（领取返水/全部领取）返回 `Rewards`。

- **订单 ID 契约**：`OrderSubmit.OrderId` 由 Gaming 平台 `lottery-engine` 在订单创建时生成（编号 1）；商户用此 ID 作为本地主键，`OrderSubmitAck` / `OrderSettleAck` 不带 order_id（平台已知）只回 `BalanceAmount`。

- **`GamingClientOptions`**：完整配置项，含 XML 注释；DI 扩展 `AddFeivooGamingClient<THandler>(services, configure)`。

- **写入语义**：`GamingSession.SendAsync` 不把 `cancellationToken` 传给 `IServerStreamWriter.WriteAsync`——一旦拿到 `_writeLock` 必须把整条消息无中断地落到 wire 上。流级别的真正终止由读循环统一负责检测和上报，避免单条 RPC 的 CT 取消把整条 bidi 会话毁掉。

- **CI/CD** `.github/workflows/release.yml`（trunk-based 滚动 publish）：
  - `push: branches: [main]` → publish `{Major}.{Minor}.{Patch}-main.{run_number}.{sha:0..7}` prerelease 包到 GitHub Packages（NuGet feed），下游 `Version="1.0.0-main.*"` floating range 自动 restore 到最新
  - `push: tags: v*` → publish `{Major}.{Minor}.{Patch}` stable 包（tag 不带 `-` 时强制 `StabilizePackageVersion=true` 走 stable）
  - `pull_request` → 仅 build/pack 验证，不 publish
  - `run_number` 单调递增段（`{base}-main.{run_number}.{sha}`）保证 NuGet floating range `1.0.0-main.*` 永远取最新合 main 的版本（不会因 sha 段字典序乱序）

- **依赖**：`Vertex.Messaging` 1.0.1、`Vertex.Transport.Grpc` 1.0.1、`Grpc.Net.Client` 2.71.0、`Microsoft.Extensions.*` 10.0.0。
