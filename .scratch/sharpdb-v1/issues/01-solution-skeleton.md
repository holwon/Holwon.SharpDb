# 01: 解决方案骨架

> 实现在本目录（`.scratch/sharpdb-v1/issues/`）。设计背景见 [`../spec.md`](../spec.md)。
> `.scratch/sharpdb-mcp/` 是设计史（wayfinder 档案，只读，同样有 `01`–`08` 编号，**勿实现**）。

**What to build:** 一个能构建、能跑测试的空壳解决方案——五个生产项目、四个测试项目、一个 `.slnx`、一条 CI 流水线。完成后 `dotnet build` 与 `dotnet test` 双绿，但**还没有任何行为**：项目齐备、引用方向正确、CI 会拦下破坏。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 五个生产项目存在且引用方向符合 ADR 0004：Provider 契约与 Schema model 独立成项目（不引 Driver、不引 parser）；Core 引 Dapper / SqlParserCS / MCP SDK 但**不引任何 Driver**；两个 Provider 各自独占自己的 Driver；可执行项目引用 Core 与全部 Provider
- [ ] 四个测试项目存在且镜像生产层级，逐项目引用对应的生产项目
- [ ] 仓库根一个 `.slnx` 收全部九个项目；`.slnx` 不与任何 `.csproj` 同目录
- [ ] 目标框架 `net8.0`
- [ ] `.vscode/settings.json` 指定默认解决方案为该 `.slnx`
- [ ] CI 单 job：安装 SDK ≥ 9.0.200 → `dotnet build` → `dotnet test`（均显式指定 `.slnx`）
- [ ] `dotnet build SharpDb.slnx` 与 `dotnet test SharpDb.slnx` 本地双绿

---

**验收说明**：本票不产生用户可见行为，交付的是"能构建的地基"。任何后续票都依赖它。
