# 08: 分发 —— 单包 global tool 与 README

**What to build:** 别人能按 README 装一次、配一段 JSON，就让自己的 AI 客户端用上 SharpDb。零源码、零编译。这是"能交付"的最后一步。

**Blocked by:** 07

**Status:** ready-for-agent

- [ ] 打包为单个 .NET global tool 包，包标识与命令名固定（`Holwon.SharpDb.Mcp` / `sharpdb-mcp`），初始版本 `0.1.0`
- [ ] 一次安装即同时获得 MySQL 与 SQLite 支持——**不拆包**
- [ ] README 含：安装命令、一份可直接复制的 MySQL 配置片段、一份可直接复制的 SQLite 配置片段
- [ ] 配置片段里命令名直接写 shim、参数为空，数据库类型与连接串放在 env 块
- [ ] README 说明"多数据库 = 多 server 条目"（一个服务器实例绑一个库）
- [ ] README 明确写出**最小权限账号才是只读的真正边界**（三层防御的第 3 层）
- [ ] README 说明如何调整日志级别（用标准配置键，不引入私有开关）
- [ ] 端到端验证：真的安装一次，按 README 配好，四个工具可用

---

**验收说明**：本票的验收是"照着 README 做能跑通"，不是"文件存在"。装包、贴配置、起客户端、调一次工具——全程不碰源码。

**已知接受的代价**：可执行程序引用全部 Provider，所以只用 SQLite 也会连带装 MySQL 驱动。这是单包换来的"装一次省心"。
