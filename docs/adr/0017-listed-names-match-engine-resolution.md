# `list_tables` 报的名字集合 = 引擎自己解析得动的名字集合

`list_tables` 对默认 schema（`SCHEMA_NAME()`，通常是 `dbo`）的对象报**裸名**，对非默认 schema 的对象报 `schema.name` **限定名**。

这不是自创规则，也不是审美选择：它是 SQL Server 自己的名字解析语义，而"报出一个引擎解析不了的名字"正是 `list_tables` 最不能犯的错——客户端会拿它去 `describe_table` 或直接写进 SQL，然后得到一个解析错误，而那个名字还是服务器自己给的。

## 实测依据

同一个连接上：

| 写法 | 结果 |
|---|---|
| `OBJECT_ID('users')`（`dbo.users`） | 解析成功 |
| `OBJECT_ID('dbo.users')` | 解析成功 |
| `OBJECT_ID('totals')`（`reporting.totals`） | **NULL** |
| `OBJECT_ID('reporting.totals')` | 解析成功 |

即：默认 schema 优先，非默认 schema 必须限定——引擎自己就是这么解析的。

PostgreSQL Provider 早已写下同一条理由（报 `search_path` 可达的对象，因为那正是名字可不加限定使用的集合），本 ADR 把 SQL Server 侧的对应规则也记成决策，免得后来者把它当成偶然。

## 顺带的两个后果

- `describe_table` **接受**限定名（`reporting.totals`），并把它按同一规则回显——客户端从 `list_tables` 拿到什么名字，就能原样交给 `describe_table`。
- 外键的 `referencedTable` 用同一条规则报（默认 schema 裸名、否则限定名），理由相同。
- 系统对象靠 `is_ms_shipped = 0` 与 `type IN ('U','V')` 排除。实测：不过滤时索引目录会返回数百张内部表（队列、账本、分配映射），它们不是这个数据库的表。

## 代价

- 同一张表在不同连接上可能报出不同名字（只要 `SCHEMA_NAME()` 不同）。这是引擎语义的直接后果，不是歧义：报出的名字在**那个连接上**一定是可用的。
- 名称里含点号的 schema 名（如 `[my.schema].[t]`）会与限定名形式混淆。本仓库不为它做转义——SQL Server 的标识符本身可以含点号，任何拼接都会在某个名字上失效，而引擎自己的解析规则才是唯一可靠的那条。
