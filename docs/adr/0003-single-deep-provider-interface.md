# Provider 是一个深接口，而不是多个小 seam

每种数据库类型实现**单个** `IDatabaseProvider`，把连接创建、方言与内省全部收在它后面；不拆成 `IDbConnectionFactory` + `ISchemaIntrospector` + `ISqlDialect` 三个 seam。

理由是这些部分从不独立变化——不存在"MySQL 的连接工厂 + SQLite 的内省器"这样的组合，拆开只会换来多个注册点、多个生命周期和一个没有消费者的组合问题，却没有多一分杠杆。MySQL 与 SQLite 确实是两个 adapter，但它们落在**同一个** seam 上。Provider 的实现内部仍可自行分层，那只是实现细节，不进接口。
