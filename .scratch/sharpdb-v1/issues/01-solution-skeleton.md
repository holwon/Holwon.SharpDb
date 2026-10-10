# 01: 解决方案骨架

> 实现在本目录（`.scratch/sharpdb-v1/issues/`）。设计背景见 [`../spec.md`](../spec.md)。
> `.scratch/sharpdb-mcp/` 是设计史（wayfinder 档案，只读，同样有 `01`–`08` 编号，**勿实现**）。

**What to build:** 一个能构建、能跑测试的空壳解决方案——五个生产项目、四个测试项目、一个 `.slnx`、一条 CI 流水线。完成后 `dotnet build` 与 `dotnet test` 双绿，但**还没有任何行为**：项目齐备、引用方向正确、CI 会拦下破坏。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] 五个生产项目存在且引用方向符合 ADR 0004：Provider 契约与 Schema model 独立成项目（不引 Driver、不引 parser）；Core 引 Dapper / SqlParserCS / MCP SDK 但**不引任何 Driver**；两个 Provider 各自独占自己的 Driver；可执行项目引用 Core 与全部 Provider
- [x] 四个测试项目存在且镜像生产层级，逐项目引用对应的生产项目
- [x] 仓库根一个 `.slnx` 收全部九个项目；`.slnx` 不与任何 `.csproj` 同目录
- [x] 目标框架 `net8.0`
- [x] `.vscode/settings.json` 指定默认解决方案为该 `.slnx`
- [x] CI 单 job：安装 SDK ≥ 9.0.200 → `dotnet build` → `dotnet test`（均显式指定 `.slnx`）
- [x] `dotnet build SharpDb.slnx` 与 `dotnet test SharpDb.slnx` 本地双绿

---

**验收说明**：本票不产生用户可见行为，交付的是"能构建的地基"。任何后续票都依赖它。

---

## Comments

**2026-10-09 — 完成。** 七条验收项全部勾选，`dotnet build SharpDb.slnx` 与 `dotnet test SharpDb.slnx` 本地双绿（0 警告 / 0 错误；测试项目目前均为 0 个用例，`dotnet test` 对空测试程序集退出码为 0）。

交付物：

- 五个生产项目在 `src/`：`SharpDb.Abstractions`（无任何引用）、`SharpDb.Core`（Abstractions + Dapper `2.1.89` + ModelContextProtocol `2.2.0` + SqlParserCS `0.6.5`）、`SharpDb.Provider.MySql`（MySqlConnector `2.6.2`）、`SharpDb.Provider.Sqlite`（Microsoft.Data.Sqlite `10.0.12`）、`SharpDb`（Host，引用 Core + 两个 Provider）
- 四个测试项目在 `tests/`，镜像生产层级，各自引用对应的生产项目
- 根目录 `SharpDb.slnx` 收全部九个项目；`.vscode/settings.json` 的 `dotnet.defaultSolution` 指向它
- `.github/workflows/build.yml`：单 job，装 SDK `9.0.200` → `dotnet build SharpDb.slnx` → `dotnet test SharpDb.slnx`
- `Directory.Build.props` 统一 `net8.0` / `Nullable` / `ImplicitUsings`

立项之外的两处选择（均已实测）：

1. **`.slnx` 的 SDK 下限是 > 8。** 实测 SDK `8.0.425` 构建 `.slnx` 报 `MSB4068`，故 CI 的 `dotnet-version` 写死 `9.0.200` 而非 `9.0.x`——避免解析到低于门槛的 9.0 补丁。
2. **测试程序集暂为空。** 本票"还没有任何行为"，测试项目先搭骨架；`dotnet test` 对无测试的程序集退出码为 0，CI 因此保持绿。首个用例在票 02/03 进场。

`Program.Main` 目前返回 0——它是 Host 的可构建外壳，配置绑定、Provider 装配、连接探测与 stdio 循环由后续票接入。

`bash tools/check-retired-identifiers.sh` 通过（本次不涉及 `.md` 词汇变更，仍跑一遍确认无回归）。

