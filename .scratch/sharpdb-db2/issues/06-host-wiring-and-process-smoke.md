# 06: Host 装配 + 进程 smoke

**What to build:** `SHARPDB__TYPE=db2` 能选中 Db2 Provider，四个 Tool 在真实装配下可用。进程级 smoke 证明的是真相：进程内测试自建 `AddMcpServer().WithTools<>()`，证明不了 `Program.cs` 的注册。

**Blocked by:** 04 (Provider 要先存在)

**Status:** ready-for-agent

- [ ] Host 项目的 `ProviderCatalog` 构造多注册一个 Db2 Provider
- [ ] Host 项目的 csproj 加 `ProjectReference` 到新 Provider
- [ ] `.slnx` 加两个新项目（Provider + 测试），检查无重复条目（`<Folder>` 包装会报 MSB4025）
- [ ] 分发包的描述与标签补上引擎名（用宽泛词，不写"N 个引擎"这类会过期的数字）
- [ ] 进程级 smoke：起真进程 + `SHARPDB__TYPE=db2` + 真实连接串，四个 Tool 可用；启动后 `Close()` stdin（EOF = 客户端断开），进程干净退出 exit 0
- [ ] Docker 不可用时 smoke skip（不 fail）

## Comments
