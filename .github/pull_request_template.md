<!--
  gaming-dotnet-sdk PR 模板
  L8CHAT 组织级 release engineering 规范，详见 H:\Workspaces\L8CHAT\CLAUDE.md §1-9。
  本仓 release.yml 在 push:main 时自动 publish {Major}.{Minor}.{Patch}-main.{sha:0..7}
  prerelease 包到 GitHub Packages；下游 floating range "X.Y.Z-main.*" 自动跟新。
  **不打 prerelease tag** —— stable tag vX.Y.Z 才是唯一发版动作。
-->

## Summary

<!-- 1-3 句话说清做了啥 + 为什么 -->

-

## Test plan

- [ ] `dotnet build ./GrpcSdk.sln -c Release` 通过
- [ ] `dotnet pack ./GrpcSdk.sln -c Release -p:Version=X.Y.Z-test.local` 本地 pack 验证
- [ ] 关键路径手测（如有 API 改动，附 client / server 调用样本）：
  -

## Changelog

> CLAUDE.md §7：每条 entry **必须**带 PR 链接 `[#N](https://github.com/L8CHAT/gaming-dotnet-sdk/pull/N)`。
> 打 stable tag `vX.Y.Z` 时才升级 `[未发布]` 节标题；**prerelease tag (`-rc.N` / `-beta.N` / `-main.{sha}`) 不开新节** —— main 滚动包不打 git tag，由 release.yml 在 `push:main` 自动 publish。

- [ ] 已在 `CHANGELOG.md` `[未发布]` 段追加变更条目
- [ ] **每条 entry 已带 PR 链接** `[#N](url)`（PR 创建后回填）
- [ ] 如有破坏性改动 / 需要下游同步迁移，已写明 `### Breaking` 分组

## Protos 同步

<!-- 如果改了 protos/ submodule，下面要勾 -->

- [ ] 不涉及 protos 改动 (skip)
- [ ] 已 bump `protos/` submodule 到 [gaming-protos main HEAD 或 stable tag]
- [ ] 已 `dotnet build` 验证 protobuf 重新生成的代码通过

## 关联

<!-- Closes #N / Refs #N / Depends-On 跨仓 PR -->

-

---

> 合并前 reviewer 检查：build 已跑过、CHANGELOG 已更新且每条带 PR 链接、breaking 变更有明确说明。
> Merge 后 release.yml 自动 publish `{Major}.{Minor}.{Patch}-main.{sha:0..7}` 包到 GitHub Packages NuGet feed。
