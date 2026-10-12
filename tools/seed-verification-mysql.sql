-- The MySQL fixture `tools/verify-installed-tool.ps1` seeds before pointing the installed tool at
-- a container. It builds the same shape as the SQLite fixture in tools/seed-verification-database.cs
-- (a table with a primary key, a second with a foreign key and a covering index, a view, two rows)
-- so every engine answers the harness's identical assertions.
--
-- The account at the end is the README's own recipe, verbatim: SELECT and nothing else. The tool is
-- only ever pointed at that account, so a write the Guard somehow let through would still be refused.

USE app;

CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    email VARCHAR(200)
);

-- The foreign key column is indexed explicitly: InnoDB would otherwise create a backing index of
-- its own, and the index list describe_table reports would then differ from SQLite's for a reason
-- that is an engine implementation detail rather than a difference in the schema.
CREATE TABLE orders (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    total DECIMAL(10,2) NOT NULL,
    CONSTRAINT fk_orders_user FOREIGN KEY (user_id) REFERENCES users (id),
    INDEX ix_orders_user_id (user_id)
);

CREATE VIEW active_users AS SELECT id, name FROM users;

INSERT INTO users (name, email) VALUES ('Alice', 'alice@example.com'), ('Bob', NULL);
INSERT INTO orders (user_id, total) VALUES (1, 10.50), (2, 20.25);

CREATE USER 'sharpdb_ro'@'%' IDENTIFIED BY 'a-strong-password';
GRANT SELECT ON app.* TO 'sharpdb_ro'@'%';
FLUSH PRIVILEGES;
