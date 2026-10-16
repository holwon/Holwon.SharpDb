# SharpDb

一个只读的 MCP 服务器，让 AI 代理能够查看关系数据库的内容并查询它，但无法修改任何数据。

SharpDb 作为 MCP 客户端的本地子进程运行，通过 stdio 使用模型上下文协议。它暴露四个工具：

| 工具 | 回答的问题 |
|---|---|
| `list_tables` | 数据库有哪些表和视图。 |
| `describe_table` | 单个表的结构：列、主键、外键、索引。 |
| `execute_query` | 只读 SQL 语句返回的行数据。 |
| `list_databases` | 连接可以看到哪些数据库。仅用于信息查询。 |

一个安装支持 **Db2**、**MariaDB**、**MySQL**、**PostgreSQL**、**SQLite**、**Oracle** 和 **SQL Server**。进程启动时从环境变量决定服务哪种数据库，而不是通过安装不同的包——因此在数据库之间切换不需要重新安装。

## 安装

安装需要 .NET 8 SDK 或更新版本。运行只需要运行时——.NET 8 或任何后续主版本。

```
dotnet tool install --global Holwon.SharpDb.Mcp
```

这会将 `sharpdb-mcp` 添加到你的 `PATH`。该包包含所有数据库类型，因此只需安装一次。

## 配置

在 MCP 客户端的配置文件中添加服务器条目——工作区级别的 `.vscode/mcp.json`，或用户级别的全局配置。

**MySQL**

```json
{
  "servers": {
    "sharpdb-mysql": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mysql",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Port=3306;Database=app;User ID=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

**MariaDB**

```json
{
  "servers": {
    "sharpdb-mariadb": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mariadb",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Port=3306;Database=app;User ID=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

连接字符串是标准的 MySqlConnector 格式，因此可以直接粘贴现有的连接字符串。只读模式由服务器添加，而不是连接字符串：编写你会给任何 MySQL 客户端的连接字符串，SharpDb 会在将连接交给任何组件之前将会话设置为引擎的只读模式。

**SQLite**

```json
{
  "servers": {
    "sharpdb-sqlite": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "sqlite",
        "SHARPDB__CONNECTIONSTRING": "Data Source=C:\\data\\app.db"
      }
    }
  }
}
```

**PostgreSQL**

```json
{
  "servers": {
    "sharpdb-postgres": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "postgresql",
        "SHARPDB__CONNECTIONSTRING": "Host=localhost;Port=5432;Database=app;Username=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

连接字符串是标准的 Npgsql 格式，因此可以直接粘贴现有的连接字符串。只读模式由服务器添加，而不是连接字符串：编写你会给任何 Npgsql 客户端的连接字符串，SharpDb 会在将连接交给任何组件之前将会话设置为引擎的只读模式。

**Oracle**

```json
{
  "servers": {
    "sharpdb-oracle": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "oracle",
        "SHARPDB__CONNECTIONSTRING": "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=XEPDB1)));User Id=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

连接字符串是标准的 ODP.NET 格式，可以是完整的 `(DESCRIPTION=…)` 形式或简短的 `host:port/service` 形式。只读模式由服务器添加：它打开一个事务并将其设置为 `SET TRANSACTION READ ONLY`，因为 Oracle 没有会话级别的只读模式。像其他引擎一样使用 `@name` 占位符编写 SQL——服务器会在 Oracle 看到它们之前将其转换为 `:name`。

`command` 是安装时添加到 `PATH` 的命令，`args` 保持为空：所有可变的内容都在 `env` 块中，因此在更改数据库时不需要编辑服务器的任何内容。

**SQL Server**

```json
{
  "servers": {
    "sharpdb-sqlserver": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mssql",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Database=app;User Id=sharpdb_ro;Password=CHANGE_ME;TrustServerCertificate=True"
      }
    }
  }
}
```

连接字符串是标准的 SqlClient 格式，因此可以直接粘贴现有的连接字符串。像其他引擎一样使用 `@name` 占位符编写 SQL。`TrustServerCertificate=True` 是你在连接到自签名证书的服务器时需要的——通常是本地开发服务器——因为驱动程序从 7.0 版本开始默认加密，否则会拒绝连接。这是开发的便利设置而不是推荐：在具有真实证书的服务器上，不要设置它。

与其他引擎不同，SQL Server **没有服务器可以设置的只读模式**，因此除了连接的只读*意图*之外，不会添加任何内容（参见[只读](#只读)）。

**Db2**

```json
{
  "servers": {
    "sharpdb-db2": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "db2",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost:50000;Database=app;UID=sharpdb_ro;Password=CHANGE_ME"
      }
    }
  }
}
```

连接字符串是标准的 IBM Data Server 格式。像其他引擎一样使用 `@name` 占位符编写 SQL——服务器会在 Db2 看到它们之前将其转换为 `?`，因为 Db2 驱动程序按位置绑定。

Db2 **没有服务器可以设置的只读模式**——甚至没有意图关键字。账户的权限是调用者和写入之间的唯一边界（参见[只读](#只读)）。

键名使用**双**下划线。这是 .NET 配置系统的层级分隔符；单个下划线会拼写不同的变量，服务器会静默地不读取它。

### 每种类型支持什么

| 类型 | 驱动程序 | 说明 |
|---|---|---|
| `db2` | Net.IBM.Data.Db2 | IBM Db2 LUW 11.5 或更新版本。针对 11.5.8 测试；没有多版本测试矩阵。 |
| `mariadb` | MySqlConnector | 针对 11.4 测试；没有多版本测试矩阵。 |
| `mysql` | MySqlConnector | — |
| `mssql` | Microsoft.Data.SqlClient | SQL Server 2016 或更新版本。针对 2022 (16.0) 测试；没有多版本测试矩阵。 |
| `oracle` | Oracle.ManagedDataAccess.Core | Oracle Database 12c 或更新版本。针对 XE 21 测试；没有多版本测试矩阵。 |
| `postgresql` | Npgsql | PostgreSQL 10 或更新版本。针对 16 测试；没有多版本测试矩阵。 |
| `sqlite` | Microsoft.Data.Sqlite | — |

PostgreSQL 的内省读取 `attidentity`，这是 10 成为最低版本的原因。Oracle 的内省读取 `IDENTITY_COLUMN`，这是 12c 成为最低版本的原因。SQL Server 的内省读取 `sys.objects` 和 `sys.extended_properties`，这是 2016 成为最低版本的原因——`DROP TABLE IF EXISTS` 和 `CREATE OR ALTER` 是在该版本中引入的。连接字符串不需要只读参数：会话被设置为什么是服务器的事务。

### 引擎之间的差异

所有工具在所有引擎上都有效。差异在于引擎自身的特性，这里记录这些差异而不是掩盖它们，因为假设相反的调用者会得到看起来正确但实际错误的答案：

| 行为 | Db2 | MariaDB | MySQL | PostgreSQL | SQLite | Oracle | SQL Server |
|---|---|---|---|---|---|---|---|
| `''` 与 NULL 不同 | 是 | 是 | 是 | 是 | 是 | **否——`''` 是 NULL** | 是 |
| `ON UPDATE` 引用操作 | **仅 `NO ACTION`** | 是 | 是 | 是 | 是 | **否——语法错误** | 是 |
| 引擎级别的只读 | **无——没有可设置的** | 会话 | 会话 | 会话 | 文件 | 事务 | **无——没有可设置的** |
| 只读模式阻止 DDL | 不适用 | 是 | 是 | 是 | 是 | **否——DDL 隐式提交** | 不适用 |
| 账户的权限是 | **唯一边界** | 建议 | 建议 | 建议 | 不适用 | **唯一边界** | **唯一边界** |
| `EXPLAIN` 返回计划 | 是 | 是 | 是 | 是 | 是 | **否——引擎报告语法错误** | **否——引擎报告语法错误** |
| `SHOW` | **否——引擎报告语法错误** | 是 | 是 | 否——引擎报告语法错误 | 否 | **否——引擎报告语法错误** | **否——引擎报告语法错误** |
| 错误消息命名对象 | 是 | 是 | 是 | 是 | 是 | **否** | 是 |
| 没有 `FROM` 的 `SELECT` | **否——语法错误** | 是 | 是 | 是 | 是 | **否（21c）——语法错误** | 是 |
| 未加引号的标识符折叠为 | 大写 | 小写 | 小写 | 小写 | 不适用 | 大写 | 小写 |
| 视图的列传播基表 | 两者都不（其余） | `NOT NULL` 和默认值（MariaDB、MySQL）；仅 `NOT NULL`（SQL Server）；两者都不（其余） |
| 目录报告的内容按权限过滤 | 否 | 否 | 否 | 不适用 | 否 | **是——`VIEW DEFINITION` 扩展它** |

在 Oracle、SQL Server 和 Db2 上，账户的权限是绕过 Guard 和写入之间的唯一屏障。Oracle 因为其只读模式是事务的属性，而 DDL 语句会提交从而逃逸（参见 [ADR 0014](docs/adr/0014-oracle-read-only-transactional-with-ddl-hole.md)）；SQL Server 因为它根本没有只读模式（参见 [ADR 0016](docs/adr/0016-sqlserver-has-no-engine-level-read-only.md)）；Db2 因为它根本没有只读模式（参见 [ADR 0019](docs/adr/0019-db2-has-no-engine-level-read-only.md)）。

在 SQL Server 上，目录报告的内容按权限过滤，因此可以读取表但缺少 `VIEW DEFINITION` 的账户看到的内容少于服务器持有的——测量上，没有它时列的默认文本读回为 null。发生这种情况时 nothing 会损坏；`list_tables` 和 `describe_table` 只是报告较少的详细信息，知道这一点比疑惑更好。

Oracle 和 Db2 还将未加引号的标识符折叠为大写，因此不别名其列的查询会以大写标签回答（`SELECT id …` 给出名为 `ID` 的列）。当标签重要时别名这些列——`SELECT id AS "id" …`。提供程序以小写形式报告名称，以便调用者可以不加引号使用它们。

### 描述报告视图的什么内容

`describe_table` 以目录持有的内容回答，视图的列报告因引擎而异。测量：MySQL 传播基表的 `NOT NULL` *和*其默认值到视图的列；SQL Server 传播 `NOT NULL` 但不传播默认值；PostgreSQL 和 SQLite 都不报告。将视图的 `isNullable` 和 `defaultValue` 读作"目录所说的"而不是"其后面的列是什么"。

在 SQL Server 上有第二个后果，这是相反的问题：视图的列继承其基列的 `IDENTITY` 标志，因此目录将具有标识列的视图的 `id` 列称为生成的。视图中 nothing 生成值，因此报告为不自动递增。

## 多个数据库

一个服务器进程绑定到一个数据库。要给代理两个数据库，添加两个条目——每个一个服务器：

```json
{
  "servers": {
    "sharpdb-app": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "mysql",
        "SHARPDB__CONNECTIONSTRING": "Server=localhost;Database=app;User ID=sharpdb_ro;Password=CHANGE_ME"
      }
    },
    "sharpdb-analytics": {
      "type": "stdio",
      "command": "sharpdb-mcp",
      "args": [],
      "env": {
        "SHARPDB__TYPE": "sqlite",
        "SHARPDB__CONNECTIONSTRING": "Data Source=C:\\data\\analytics.db"
      }
    }
  }
}
```

每个条目都有自己的进程和自己的四个工具副本。这是到达第二个数据库的唯一方式：没有工具接受数据库参数，`list_databases` 只报告连接可以看到的内容。

## 配置参考

每个值都来自 `env` 块。没有配置文件可以查找，也没有命令行参数。

| 键 | 必需 | 默认值 | 含义 |
|---|---|---|---|
| `SHARPDB__TYPE` | 是 | — | `db2`、`mariadb`、`mysql`、`mssql`、`oracle`、`postgresql` 或 `sqlite`。比较时忽略大小写和周围的空格。 |
| `SHARPDB__CONNECTIONSTRING` | 是 | — | 传递给服务此数据库类型的驱动程序。 |
| `SHARPDB__DEFAULTMAXROWS` | 否 | `100` | 查询未请求数字时返回的行数。 |
| `SHARPDB__MAXROWS` | 否 | `1000` | 查询不能提高只能降低的上限。 |
| `SHARPDB__TIMEOUTSECONDS` | 否 | `30` | 查询在被放弃之前可以运行的时间。 |

## 只读

nothing 写入你的数据库，这在三个地方而不是一个地方强制执行。

1. **Guard。** 代理发送的每条语句在运行之前都会被解析。只允许 `SELECT`、`EXPLAIN` 和 `SHOW`；其他任何内容在到达数据库之前都会被拒绝，拒绝会告诉代理可以改用哪些内容。读取数据库外部的函数——服务器的文件、另一个服务器——也会按引擎被拒绝。
2. **引擎自身的只读模式。** 在引擎有的地方，它在连接交给任何组件之前被打开：MySQL 和 MariaDB 会话被设置为只读，PostgreSQL 会话被置于引擎的事务只读模式，SQLite 文件以只读方式打开，Oracle 连接在只读事务中打开。Guard 以某种方式放过的语句仍然会被数据库拒绝。
3. **数据库账户。** 这是实际保持的边界。

第二层不是通用的，说明这一点是承诺的一部分而不是对它的警告。SQL Server 没有服务器可以设置的只读模式，因此其连接携带的是只读**意图**——在 Always On 可读副本上是真实边界，在独立实例上则什么都没有。它在上面的矩阵中显示为 `none` 而不是悄悄省略，因为假设相反的调用者会假设一个不存在的保护。

### 账户是真实边界

前两层防止错误和检查中的漏洞。它们是代码，代码可能有漏洞。唯一不能被绕过的限制是你交出的账户的权限。

给 SharpDb 一个只能读取的专用账户。对于数据库 `app`：

```sql
CREATE USER 'sharpdb_ro'@'%' IDENTIFIED BY 'a-strong-password';
GRANT SELECT ON app.* TO 'sharpdb_ro'@'%';
```

`SELECT` 足以满足所有四个工具，包括读取视图。不需要 `INSERT`、`UPDATE`、`DELETE`、`CREATE`、`DROP`、`ALTER` 或 `FILE`——因此即使某些内容被放过，数据库也会拒绝它。

在 Oracle 和 SQL Server 上，账户的权限不是建议而是边界，因此服务器连接到的账户是整个只读保证。该账户由你选择：SharpDb 接受你编写的连接字符串，不检查、验证或重写它。

在 SQL Server 上，如果你希望完整而不是部分读取目录，该账户除了 `SELECT` 之外还需要 `VIEW DEFINITION`——在 2022 上测量，没有它时列的默认文本不可见并读回为 null。没有它 nothing 会停止工作；模式报告的内容少于服务器持有的。

对于 SQLite 没有要限制的账户：文件自身的权限是边界，因此如果文件重要，将 SharpDb 指向副本或只读路径。

### 服务器从不暴露什么

- 连接字符串在任何日志级别都不会被记录。可能携带密码的内容都不会到达输出。
- 作为查询参数发送的行值不会被记录。语句本身被记录，因此你可以看到被请求的内容。
- 启动失败命名原因而不重复连接字符串。

## 日志

SharpDb 记录到 stderr——stdout 属于协议——使用标准的 .NET 日志键：

```json
"env": {
  "SHARPDB__TYPE": "sqlite",
  "SHARPDB__CONNECTIONSTRING": "Data Source=C:\\data\\app.db",
  "Logging__LogLevel__Default": "Debug"
}
```

`Information` 是默认值。当某些内容未按预期调用时将其提高到 `Debug`；将其降低到 `Warning` 以保持客户端的日志安静。语句在 `Information` 级别记录，失败的调用在 `Warning` 级别记录。

## 当它无法启动时

SharpDb 在启动时打开一个连接，如果有任何问题会**立即退出**，向 stderr 写入一行你的客户端会显示给你的内容。启动然后在每次调用时失败的服务器更难诊断，因此这是故意的。

| 你看到的 | 它的含义 |
|---|---|
| `sharpdb: SHARPDB__TYPE is required (expected "db2" or "mariadb" or "mysql" or "mssql" or "oracle" or "postgresql" or "sqlite").` | `env` 块缺少类型，或用单个下划线拼写。 |
| `sharpdb: SHARPDB__TYPE 'postgres' is unknown (expected "db2" or "mariadb" or "mysql" or "mssql" or "oracle" or "postgresql" or "sqlite").` | 给出了没有数据库类型服务的类型。`postgres` 不是别名；类型拼写为 `postgresql`。 |
| `sharpdb: SHARPDB__CONNECTIONSTRING is required and must not be blank.` | 连接字符串缺失或为空。 |
| `sharpdb: Could not connect to the mysql database: …` | 服务器无法连接——通常是凭据、主机或数据库名称。 |
| `sharpdb: Invalid configuration: Failed to convert configuration value 'plenty' at 'SHARPDB:MaxRows' to type 'System.Int32'.` | 行限制或超时不是数字。 |

原因总是一行，它总是以 `sharpdb: ` 开头，它从不包含连接字符串。

## 许可证

SharpDb 根据 [GNU General Public License, version 3](LICENSE) (GPL-3.0) 许可。

你可以商业使用它。如果你分发它或基于它构建的任何内容——包括对它的包装——你必须在同一许可证下发布该工作的源代码。

本项目使用 Oracle Managed Data Access，根据 Oracle Free Distribution Terms 许可。参见 [NOTICE](NOTICE) 获取第三方许可通知。
