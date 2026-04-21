# 兼容性说明

## SDK 与协议版本对照

| SDK 版本 | 兼容 Proto 仓库版本 | 备注 |
|---|---|---|
| 1.0.0 | gaming-protos 1.0.0 | 首个正式版本 |

## .NET 目标框架

| 包 | 目标框架 |
|---|---|
| `Feivoo.Gaming.Grpc` | netstandard2.0 |
| `Feivoo.Gaming.GrpcClient` | net10.0 |
| `Feivoo.Gaming.GrpcServer.Abstractions` | net10.0 |

## 主要依赖版本

| 依赖包 | 版本 |
|---|---|
| `Grpc.Net.Client` | 2.71.0 |
| `Grpc.AspNetCore.Server` | 2.71.0 |
| `Grpc.Core.Api` | 2.71.0 |
| `Google.Protobuf` | 3.29.3 |
| `Google.Api.CommonProtos` | 2.16.0 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.0 |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.0 |
| `Microsoft.Extensions.Options` | 10.0.0 |

## Proto 变更兼容策略

Protobuf 以字段编号（field number）而非字段名进行序列化，因此：

| 变更类型 | 是否向后兼容 | 说明 |
|---|---|---|
| 新增字段 | 是 | 旧版 SDK 会静默忽略未知字段 |
| 废弃字段 | 是 | 标记 `reserved`，编号永久保留不复用 |
| 重命名字段 | 是（序列化层） | 编号不变则序列化兼容，但 SDK C# 属性名需同步更新 |
| 删除字段 | **否** | 破坏性变更，主版本号须递增 |
| 修改字段类型 | **否** | 破坏性变更，主版本号须递增 |
| 修改字段编号 | **否** | 破坏性变更，主版本号须递增 |

## 破坏性变更策略

- Proto 发生破坏性变更时，SDK 主版本号（`MajorVersion`）递增
- `UPGRADE.md` 同步更新迁移指南
- 旧版 SDK 在至少一个迁移周期内继续维护安全补丁
