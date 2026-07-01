# sqlite-mcp

A tiny [MCP](https://modelcontextprotocol.io) server, written in Zig, that hands
an LLM a SQLite database and gets out of the way.

There are exactly **two tools**:

| Tool          | What it does                                                                 |
| ------------- | ---------------------------------------------------------------------------- |
| `execute_sql` | Runs whatever SQL you send. No validation, no sandbox. Returns rows or counts as JSON. |
| `get_schema`  | Returns every table/view/index/trigger's `CREATE` statement, so a new session can learn what exists. |

The database is a plain file on disk, so data and schema survive across
sessions. There is deliberately no query allow-list, no read-only mode, and no
statement limit — the model sends what it wants and it runs.

## Requirements

- [Zig](https://ziglang.org) 0.14.x
- SQLite dev library (`libsqlite3-dev` on Debian/Ubuntu, `sqlite` on Homebrew).
  The server links the system `sqlite3` via its C API.

## Build

```sh
zig build              # produces ./zig-out/bin/sqlite-mcp
zig build -Doptimize=ReleaseSafe   # optimized, keeps safety checks
```

## Run

The server speaks JSON-RPC 2.0 over stdio (the MCP stdio transport). The
database path is chosen in this order:

1. first command-line argument
2. `SQLITE_DB_PATH` environment variable
3. `sqlite_mcp.db` in the current directory

```sh
zig-out/bin/sqlite-mcp /path/to/my.db
```

Diagnostics go to **stderr**; the protocol stream is stdout only.

## Wiring it into an MCP client

For Claude Desktop / Claude Code, add a server entry pointing at the built
binary (use an absolute path):

```json
{
  "mcpServers": {
    "sqlite": {
      "command": "/absolute/path/to/SQL-for-llm/zig-out/bin/sqlite-mcp",
      "args": ["/absolute/path/to/my.db"]
    }
  }
}
```

## Try it without a client

Pipe newline-delimited JSON-RPC straight in:

```sh
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"execute_sql","arguments":{"sql":"CREATE TABLE t(x); INSERT INTO t VALUES (1),(2);"}}}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"execute_sql","arguments":{"sql":"SELECT * FROM t;"}}}' \
  '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_schema","arguments":{}}}' \
  | zig-out/bin/sqlite-mcp /tmp/demo.db
```

## Result shapes

`execute_sql` returns a JSON document (as text) with one entry per statement:

```json
{
  "ok": true,
  "statements": [
    { "columns": ["id","name"], "rows": [[1,"alice"]], "row_count": 1, "statement": "SELECT ..." },
    { "rows_affected": 2, "statement": "INSERT ..." }
  ],
  "total_changes": 2
}
```

On a SQL error the tool result has `isError: true` and the text is the SQLite
error message.

`get_schema` returns `object_count`, an `objects` array (`type` / `name` /
`sql`), and a concatenated `ddl` string.

## Notes / limitations

- SQLite dynamic types map to JSON: INTEGER → number, REAL → number, TEXT →
  string, NULL → null, BLOB → `"<BLOB N bytes>"` (blobs are summarized, not
  serialized).
- Multiple `;`-separated statements in one `execute_sql` call are supported.
- Each statement autocommits; there is no explicit transaction management tool
  (you can still send `BEGIN` / `COMMIT` yourself).
