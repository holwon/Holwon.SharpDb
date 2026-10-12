# 03: PostgreSQL `describe_table`：列与主键

**What to build:**

描述一张 PostgreSQL 表时，代理能看到它全部的列与主键：列按声明顺序、序号 **1 起**、给出**原始类型声明**（如 `character varying(255)`、`numeric(10,2)`、`timestamp with time zone`）、nullability、默认值表达式、以及自增标记（`GENERATED … AS IDENTITY` 与遗留 `serial` 都要认出来）；主键按**键序**给出（复合键时不是声明序）。表名匹配按 PostgreSQL 自身的标识符规则（未加引号折叠为小写），猜错大小写也能命中；名字不存在时明确回"没有"而不是报错。

**Blocked by:** 02

**Status:** ready-for-agent

- [ ] 列以声明顺序返回，序号从 1 起，与另两种引擎的答案同形。
- [ ] 类型是**原始声明**（保留长度/精度/时区），不是被拆开的基类型名。
- [ ] nullability 与默认值如实报告。
- [ ] 自增：`GENERATED … AS IDENTITY` 与 `serial`（默认值为 `nextval(...)`）都被识别为自增。
- [ ] 复合主键按键序返回（例如声明为 `(b, a)` 的键给出 `b, a`）。
- [ ] 大小写不敏感的名字匹配；不存在的名字回 `null`。
- [ ] 契约套件中列与主键的断言在 PostgreSQL 上通过（含复合键那条）。
