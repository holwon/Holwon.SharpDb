# 06: Host 装配 + 进程级 smoke

**What to build:** `SHARPDB__TYPE=mssql` 在**真进程**里可启动并服务四个 Tool。进程内测试证明不了宿主的接线，所以必须起进程。

**Blocked by:** 05 (测试必须先通过)

**Status:** resolved

- [x] 装配目录注册 Provider（一行）
- [x] 分发包引用 Provider 项目，使 Provider 随同一个包分发
- [x] 进程级 smoke：`mssql` 类型被接受；数据库不可达时报**一行** stderr 并 fail fast（与既有引擎同形）
- [x] 未知类型的报错文案在列出预期值时含 `mssql`（该文案由注册的 Provider 动态拼出，因此断言的是实际拼出的结果）
- [x] stdin EOF → 干净退出 exit 0
- [x] 分发包仍只产出一个包，且包内同时含 mssql Provider

## Comments

### 2026-10-13 — 完成（`8fd58ff`）

`Program.cs` 多注册一行 `new MsSqlDatabaseProvider()`；`SharpDb.csproj` 多一条 ProjectReference。

预期类型清单由注册的 Provider **动态拼出**，所以文案本身没改；只有逐字写着类型名的那条断言补了 `"mssql"`。两条新进程级 smoke：类型被接受且到达真 TDS 驱动（`Could not connect to the mssql database`）、失败消息不含密码。

**打包实测**：`dotnet pack` 产出**恰好 1 个** `.nupkg`（`Holwon.SharpDb.Mcp.0.2.1.nupkg`），内含 `SharpDb.Provider.MsSql.dll` 与 `Microsoft.Data.SqlClient.dll`（含各语言资源与 SNI 原生库），共 142 个条目。

`stdin EOF → exit 0`：已有覆盖（`Startup_against_a_database_that_is_available_succeeds_and_leaves_stdout_clean` 与 `LoggingContractTests` 的 5 条都断言 exit 0，而 `HostProcess.RunAsync` 每次都关 stdin），且该行为与引擎无关，未新增重复用例。
