# SharpDb

一个以只读方式把关系型数据库暴露给 AI 代理的 MCP 服务器：让模型理解数据库结构并执行只读查询，通过 Provider 扩展新的数据库类型。

## Language

### 扩展单元

**Provider**：
一种数据库类型的完整实现——负责建立连接、生成该方言的内省查询、把原生元数据映射为统一的 Schema model。新增一种数据库 = 新增一个 Provider。
_Avoid_：Driver、Connector、Adapter、Plugin

**Driver**：
Provider 底层使用的第三方 ADO.NET 包（如 MySqlConnector、Microsoft.Data.Sqlite）。Driver 是实现细节，只被 Provider 引用；讨论"支持某个数据库"时说的是 Provider，不是 Driver。
_Avoid_：用 Driver 指代 Provider

### 结构表示

**Schema model**：
对数据库结构的、与具体数据库无关的统一表示——表、列、主键、外键、索引。每个 Provider 把自己的原生元数据映射到它。
_Avoid_：Metadata、DDL model

**Introspection**：
读取数据库自身元数据、以生成 Schema model 的过程。
_Avoid_：Discovery、Reflection

### 只读

**Guard**：
应用层的只读检查——在执行前解析 SQL，拒绝非只读语句。它是面向 AI 的快速反馈层，负责给出清晰的拒绝理由。
_Avoid_：Validator、Filter

**Read-only enforcement**：
数据库引擎层的只读保证——即使 Guard 被绕过，写操作仍会被数据库自身拒绝。
_Avoid_：用 Guard 指代它

### 对外接口

**Tool**：
服务器暴露给 AI 客户端的操作。v1 恰好四个：`execute_query`、`list_tables`、`describe_table`、`list_databases`。
_Avoid_：Command、Endpoint、Function
