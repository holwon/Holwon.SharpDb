# 06: Host 装配 + 进程级 smoke

**What to build:** `SHARPDB__TYPE=mssql` 在**真进程**里可启动并服务四个 Tool。进程内测试证明不了宿主的接线，所以必须起进程。

**Blocked by:** 05 (测试必须先通过)

**Status:** ready-for-agent

- [ ] 装配目录注册 Provider（一行）
- [ ] 分发包引用 Provider 项目，使 Provider 随同一个包分发
- [ ] 进程级 smoke：`mssql` 类型被接受；数据库不可达时报**一行** stderr 并 fail fast（与既有引擎同形）
- [ ] 未知类型的报错文案在列出预期值时含 `mssql`（该文案由注册的 Provider 动态拼出，因此断言的是实际拼出的结果）
- [ ] stdin EOF → 干净退出 exit 0
- [ ] 分发包仍只产出一个包，且包内同时含 mssql Provider
