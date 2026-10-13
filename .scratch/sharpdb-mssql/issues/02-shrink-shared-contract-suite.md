# 02: 共享契约套件收缩

**What to build:** 共享契约套件只保留每个引擎都能自己满足的断言。唯一直接验证引擎层只读（第 2 层）的那条断言 —— 打开连接后试着写一次并断言引擎拒绝 —— 下沉到 MySQL / PostgreSQL / SQLite / Oracle 四个引擎各自的测试项目。覆盖零丢失，只是换位置。此票完成后，第五个引擎才能加入共享套件。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `The_connection_a_provider_opens_refuses_a_write` 从共享套件移出
- [ ] 该断言在 MySQL / PostgreSQL / SQLite / Oracle 四个引擎的测试项目里各有一份等价断言
- [ ] 四个引擎的测试项目**仍然全绿**（回归验证：断言换了位置，行为没变）
- [ ] 共享套件注释更新，说明"只保留每个引擎都能自己满足的断言"，并点明第 2 层不是每个引擎都有 —— 这是本票存在的理由，也是下个引擎加入时的依据
