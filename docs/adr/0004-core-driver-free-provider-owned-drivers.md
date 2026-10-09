# Core 不引用 Driver，Driver 由 Provider 拥有，Host 负责组装

Core 是一个库，不引用 `MySqlConnector` / `Microsoft.Data.Sqlite`；每个 Provider 独占自己底层的 Driver；可执行程序（Host）引用全部 Provider，并在启动时按 `SHARPDB__TYPE` 用普通 .NET DI 装配其中一个。

只有把"选哪个 Provider"推到装配层，Core 才能真正保持数据库无关。代价是新增一种数据库需要改 Host 的装配（一行注册），但 **Core 零改动**——这正是"新增一种数据库 = 新增一个 Provider 项目"的含义。装配刻意使用显式 DI 注册而非 `[assembly:]` 反射扫描：反射会带来 trim 不安全、装配错误静默失败、调试困难，而省下的那行注册并不值得。
