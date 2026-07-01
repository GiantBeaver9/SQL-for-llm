//! A dead-simple MCP (Model Context Protocol) server that exposes a SQLite
//! database to an LLM over stdio. It speaks JSON-RPC 2.0 with newline-delimited
//! messages (the MCP stdio transport) and offers exactly two tools:
//!
//!   * execute_sql  - run arbitrary SQL, get back rows / rows-affected as JSON
//!   * get_schema   - dump every table/view/index/trigger's CREATE statement
//!
//! There is deliberately no query validation or sandboxing: the LLM sends
//! whatever SQL it wants and we run it. The database file persists on disk so
//! the schema (and data) survive across sessions.
//!
//! Written in Zig against the SQLite C API. All protocol/log output rules:
//!   * JSON-RPC responses go to stdout, one message per line.
//!   * Diagnostics go to stderr. NEVER write logs to stdout, it corrupts the
//!     protocol stream.

const std = @import("std");

const c = @cImport({
    @cInclude("sqlite3.h");
});

const Value = std.json.Value;
const ObjectMap = std.json.ObjectMap;
const Array = std.json.Array;
const Allocator = std.mem.Allocator;

/// The single, process-wide database connection. Opened once at startup and
/// reused for every request so state persists within a session; the file on
/// disk persists across sessions.
var db: ?*c.sqlite3 = null;

const SERVER_NAME = "sqlite-mcp";
const SERVER_VERSION = "0.1.0";
const DEFAULT_PROTOCOL = "2024-11-05";
const MAX_MESSAGE_BYTES = 64 * 1024 * 1024;

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    defer _ = gpa.deinit();
    const base = gpa.allocator();

    const db_path = try resolveDbPath(base);
    defer base.free(db_path);

    if (c.sqlite3_open(db_path.ptr, &db) != c.SQLITE_OK) {
        log("fatal: could not open database '{s}': {s}", .{ db_path, std.mem.span(c.sqlite3_errmsg(db)) });
        _ = c.sqlite3_close(db);
        return error.OpenFailed;
    }
    defer _ = c.sqlite3_close(db);

    // A short busy timeout keeps us from erroring out the instant another
    // connection holds a lock; nice for a casual multi-tool setup.
    _ = c.sqlite3_busy_timeout(db, 5000);

    log("{s} v{s} ready; database = '{s}'", .{ SERVER_NAME, SERVER_VERSION, db_path });

    const stdin = std.io.getStdIn().reader();
    const stdout = std.io.getStdOut().writer();

    // Each incoming JSON-RPC message is a single line. We hand every message
    // its own arena so cleanup is a single deinit() no matter how much scratch
    // JSON we allocate while handling it.
    while (true) {
        const maybe_line = stdin.readUntilDelimiterOrEofAlloc(base, '\n', MAX_MESSAGE_BYTES) catch |err| {
            log("read error: {s}; shutting down", .{@errorName(err)});
            break;
        };
        const line = maybe_line orelse break; // EOF: client closed the pipe.
        defer base.free(line);

        const trimmed = std.mem.trim(u8, line, " \t\r\n");
        if (trimmed.len == 0) continue;

        var arena = std.heap.ArenaAllocator.init(base);
        defer arena.deinit();

        handleMessage(arena.allocator(), trimmed, stdout) catch |err| {
            log("error handling message: {s}", .{@errorName(err)});
        };
    }
}

/// Decide where the database lives. Precedence:
///   1. first command-line argument
///   2. SQLITE_DB_PATH environment variable
///   3. "sqlite_mcp.db" in the current working directory
/// The result is NUL-terminated for the SQLite C API.
fn resolveDbPath(a: Allocator) ![:0]u8 {
    const args = try std.process.argsAlloc(a);
    defer std.process.argsFree(a, args);
    if (args.len > 1 and args[1].len > 0) {
        return a.dupeZ(u8, args[1]);
    }

    if (std.process.getEnvVarOwned(a, "SQLITE_DB_PATH")) |v| {
        defer a.free(v);
        if (v.len > 0) return a.dupeZ(u8, v);
    } else |_| {}

    return a.dupeZ(u8, "sqlite_mcp.db");
}

fn handleMessage(a: Allocator, line: []const u8, out: anytype) !void {
    const parsed = std.json.parseFromSliceLeaky(Value, a, line, .{}) catch {
        try sendError(a, out, Value{ .null = {} }, -32700, "Parse error");
        return;
    };

    if (parsed != .object) {
        try sendError(a, out, Value{ .null = {} }, -32600, "Invalid Request: expected a JSON object");
        return;
    }
    const obj = parsed.object;

    const method_v = obj.get("method") orelse return; // Not a request; ignore.
    if (method_v != .string) return;
    const method = method_v.string;

    // Absent id => notification (no response expected).
    const id_opt = obj.get("id");

    if (std.mem.eql(u8, method, "initialize")) {
        try handleInitialize(a, out, obj, id_opt);
    } else if (std.mem.eql(u8, method, "tools/list")) {
        try handleToolsList(a, out, id_opt);
    } else if (std.mem.eql(u8, method, "tools/call")) {
        try handleToolsCall(a, out, obj, id_opt);
    } else if (std.mem.eql(u8, method, "ping")) {
        try sendResult(a, out, id_opt orelse Value{ .null = {} }, Value{ .object = ObjectMap.init(a) });
    } else if (std.mem.startsWith(u8, method, "notifications/") or std.mem.eql(u8, method, "initialized")) {
        // Notifications carry no id and expect no reply.
        return;
    } else if (id_opt) |id| {
        if (id != .null) try sendError(a, out, id, -32601, "Method not found");
    }
}

fn handleInitialize(a: Allocator, out: anytype, obj: ObjectMap, id_opt: ?Value) !void {
    const id = id_opt orelse Value{ .null = {} };

    // Echo the client's protocol version if it named one; otherwise fall back.
    var proto: []const u8 = DEFAULT_PROTOCOL;
    if (obj.get("params")) |p| {
        if (p == .object) {
            if (p.object.get("protocolVersion")) |pv| {
                if (pv == .string) proto = pv.string;
            }
        }
    }

    var caps = ObjectMap.init(a);
    try caps.put("tools", Value{ .object = ObjectMap.init(a) });

    var info = ObjectMap.init(a);
    try info.put("name", Value{ .string = SERVER_NAME });
    try info.put("version", Value{ .string = SERVER_VERSION });

    var result = ObjectMap.init(a);
    try result.put("protocolVersion", Value{ .string = proto });
    try result.put("capabilities", Value{ .object = caps });
    try result.put("serverInfo", Value{ .object = info });

    try sendResult(a, out, id, Value{ .object = result });
}

const TOOLS_JSON =
    \\[
    \\  {
    \\    "name": "execute_sql",
    \\    "description": "Execute one or more SQL statements against the SQLite database. Accepts any SQL: SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, PRAGMA, multiple statements separated by ';', etc. Returns rows for queries that produce them and rows-affected counts for statements that modify data. Changes are committed immediately.",
    \\    "inputSchema": {
    \\      "type": "object",
    \\      "properties": {
    \\        "sql": { "type": "string", "description": "The SQL to run. May contain multiple ';'-separated statements." }
    \\      },
    \\      "required": ["sql"]
    \\    }
    \\  },
    \\  {
    \\    "name": "get_schema",
    \\    "description": "Return the full schema of the database: the CREATE statement for every table, view, index, and trigger. Use this at the start of a session to learn what data exists and how it is structured.",
    \\    "inputSchema": {
    \\      "type": "object",
    \\      "properties": {}
    \\    }
    \\  }
    \\]
;

fn handleToolsList(a: Allocator, out: anytype, id_opt: ?Value) !void {
    const id = id_opt orelse Value{ .null = {} };
    const tools = try std.json.parseFromSliceLeaky(Value, a, TOOLS_JSON, .{});

    var result = ObjectMap.init(a);
    try result.put("tools", tools);
    try sendResult(a, out, id, Value{ .object = result });
}

fn handleToolsCall(a: Allocator, out: anytype, obj: ObjectMap, id_opt: ?Value) !void {
    const id = id_opt orelse Value{ .null = {} };

    const params_v = obj.get("params") orelse {
        try sendError(a, out, id, -32602, "Invalid params: missing params");
        return;
    };
    if (params_v != .object) {
        try sendError(a, out, id, -32602, "Invalid params: params must be an object");
        return;
    }
    const params = params_v.object;

    const name_v = params.get("name") orelse {
        try sendError(a, out, id, -32602, "Invalid params: missing tool name");
        return;
    };
    if (name_v != .string) {
        try sendError(a, out, id, -32602, "Invalid params: tool name must be a string");
        return;
    }
    const name = name_v.string;
    const arguments = params.get("arguments");

    if (std.mem.eql(u8, name, "execute_sql")) {
        const sql = getStringArg(arguments, "sql") orelse {
            try toolTextResult(a, out, id, "Error: 'sql' argument is required and must be a string.", true);
            return;
        };
        const res = try executeSql(a, sql);
        try toolTextResult(a, out, id, res.text, res.is_error);
    } else if (std.mem.eql(u8, name, "get_schema")) {
        const text = try getSchema(a);
        try toolTextResult(a, out, id, text, false);
    } else {
        const msg = try std.fmt.allocPrint(a, "Error: unknown tool '{s}'.", .{name});
        try toolTextResult(a, out, id, msg, true);
    }
}

fn getStringArg(arguments: ?Value, key: []const u8) ?[]const u8 {
    const args = arguments orelse return null;
    if (args != .object) return null;
    const v = args.object.get(key) orelse return null;
    if (v != .string) return null;
    return v.string;
}

const SqlOutcome = struct {
    text: []const u8,
    is_error: bool,
};

/// Run whatever SQL the client sent us. Handles multiple ';'-separated
/// statements by walking the string with sqlite3_prepare_v2's tail pointer.
/// Result is a JSON document (as text) describing each statement's outcome.
fn executeSql(a: Allocator, sql: []const u8) !SqlOutcome {
    const changes_before = c.sqlite3_total_changes(db);

    var statements = Array.init(a);

    var head: [*c]const u8 = sql.ptr;
    const end = @intFromPtr(sql.ptr) + sql.len;

    while (@intFromPtr(head) < end) {
        var stmt: ?*c.sqlite3_stmt = null;
        var tail: [*c]const u8 = null;
        const remaining: c_int = @intCast(end - @intFromPtr(head));

        if (c.sqlite3_prepare_v2(db, head, remaining, &stmt, &tail) != c.SQLITE_OK) {
            const msg = std.mem.span(c.sqlite3_errmsg(db));
            return .{ .text = try std.fmt.allocPrint(a, "SQL error: {s}", .{msg}), .is_error = true };
        }

        // A NULL statement with SQLITE_OK means trailing whitespace or a
        // comment with no actual statement: nothing to run, just advance.
        if (stmt == null) {
            head = tail;
            continue;
        }

        var stmt_obj = ObjectMap.init(a);
        const col_count = c.sqlite3_column_count(stmt);

        if (col_count > 0) {
            var columns = Array.init(a);
            var i: c_int = 0;
            while (i < col_count) : (i += 1) {
                const cname = std.mem.span(c.sqlite3_column_name(stmt, i));
                try columns.append(Value{ .string = try a.dupe(u8, cname) });
            }

            var rows = Array.init(a);
            while (true) {
                const step = c.sqlite3_step(stmt);
                if (step == c.SQLITE_ROW) {
                    var row = Array.init(a);
                    i = 0;
                    while (i < col_count) : (i += 1) {
                        try row.append(try columnValue(a, stmt, i));
                    }
                    try rows.append(Value{ .array = row });
                } else if (step == c.SQLITE_DONE) {
                    break;
                } else {
                    const msg = std.mem.span(c.sqlite3_errmsg(db));
                    _ = c.sqlite3_finalize(stmt);
                    return .{ .text = try std.fmt.allocPrint(a, "SQL error during execution: {s}", .{msg}), .is_error = true };
                }
            }

            try stmt_obj.put("columns", Value{ .array = columns });
            try stmt_obj.put("rows", Value{ .array = rows });
            try stmt_obj.put("row_count", Value{ .integer = @intCast(rows.items.len) });
        } else {
            // No result columns: DDL/DML/PRAGMA. Step once to execute it.
            const step = c.sqlite3_step(stmt);
            if (step != c.SQLITE_DONE and step != c.SQLITE_ROW) {
                const msg = std.mem.span(c.sqlite3_errmsg(db));
                _ = c.sqlite3_finalize(stmt);
                return .{ .text = try std.fmt.allocPrint(a, "SQL error during execution: {s}", .{msg}), .is_error = true };
            }
            try stmt_obj.put("rows_affected", Value{ .integer = @intCast(c.sqlite3_changes(db)) });
        }

        if (c.sqlite3_sql(stmt)) |txt| {
            try stmt_obj.put("statement", Value{ .string = try a.dupe(u8, std.mem.span(txt)) });
        }

        _ = c.sqlite3_finalize(stmt);
        try statements.append(Value{ .object = stmt_obj });
        head = tail;
    }

    var top = ObjectMap.init(a);
    try top.put("ok", Value{ .bool = true });
    try top.put("statements", Value{ .array = statements });
    try top.put("total_changes", Value{ .integer = @intCast(c.sqlite3_total_changes(db) - changes_before) });

    return .{ .text = try stringify(a, Value{ .object = top }), .is_error = false };
}

/// Convert the current column of a stepped statement into a JSON value,
/// mapping SQLite's dynamic types onto JSON ones.
fn columnValue(a: Allocator, stmt: ?*c.sqlite3_stmt, i: c_int) !Value {
    return switch (c.sqlite3_column_type(stmt, i)) {
        c.SQLITE_INTEGER => Value{ .integer = @intCast(c.sqlite3_column_int64(stmt, i)) },
        c.SQLITE_FLOAT => Value{ .float = c.sqlite3_column_double(stmt, i) },
        c.SQLITE_TEXT => blk: {
            const ptr = c.sqlite3_column_text(stmt, i);
            const len: usize = @intCast(c.sqlite3_column_bytes(stmt, i));
            break :blk Value{ .string = try a.dupe(u8, ptr[0..len]) };
        },
        c.SQLITE_BLOB => blk: {
            const len = c.sqlite3_column_bytes(stmt, i);
            break :blk Value{ .string = try std.fmt.allocPrint(a, "<BLOB {d} bytes>", .{len}) };
        },
        else => Value{ .null = {} }, // SQLITE_NULL
    };
}

/// Dump every user-defined schema object's CREATE statement, plus a machine
/// readable list. Ordered tables -> views -> indexes -> triggers.
fn getSchema(a: Allocator) ![]const u8 {
    const query =
        "SELECT type, name, sql FROM sqlite_master " ++
        "WHERE name NOT LIKE 'sqlite_%' AND sql IS NOT NULL " ++
        "ORDER BY CASE type WHEN 'table' THEN 0 WHEN 'view' THEN 1 WHEN 'index' THEN 2 WHEN 'trigger' THEN 3 ELSE 4 END, name;";

    var stmt: ?*c.sqlite3_stmt = null;
    if (c.sqlite3_prepare_v2(db, query.ptr, @intCast(query.len), &stmt, null) != c.SQLITE_OK) {
        return std.fmt.allocPrint(a, "Error reading schema: {s}", .{std.mem.span(c.sqlite3_errmsg(db))});
    }
    defer _ = c.sqlite3_finalize(stmt);

    var ddl = std.ArrayList(u8).init(a);
    var objects = Array.init(a);

    while (c.sqlite3_step(stmt) == c.SQLITE_ROW) {
        const type_ = try colText(a, stmt, 0);
        const name = try colText(a, stmt, 1);
        const sql = try colText(a, stmt, 2);

        try ddl.appendSlice(sql);
        try ddl.appendSlice(";\n\n");

        var entry = ObjectMap.init(a);
        try entry.put("type", Value{ .string = type_ });
        try entry.put("name", Value{ .string = name });
        try entry.put("sql", Value{ .string = sql });
        try objects.append(Value{ .object = entry });
    }

    var top = ObjectMap.init(a);
    try top.put("object_count", Value{ .integer = @intCast(objects.items.len) });
    if (objects.items.len == 0) {
        try top.put("note", Value{ .string = "The database is empty. Use execute_sql with CREATE TABLE ... to get started." });
    }
    try top.put("objects", Value{ .array = objects });
    try top.put("ddl", Value{ .string = try ddl.toOwnedSlice() });

    return stringify(a, Value{ .object = top });
}

/// Read column `i` of the current row as an owned, arena-allocated string.
/// NULL becomes an empty string.
fn colText(a: Allocator, stmt: ?*c.sqlite3_stmt, i: c_int) ![]const u8 {
    const ptr = c.sqlite3_column_text(stmt, i);
    if (ptr == null) return a.dupe(u8, "");
    const len: usize = @intCast(c.sqlite3_column_bytes(stmt, i));
    return a.dupe(u8, ptr[0..len]);
}

// --- JSON-RPC framing helpers ------------------------------------------------

fn stringify(a: Allocator, value: Value) ![]const u8 {
    var buf = std.ArrayList(u8).init(a);
    try std.json.stringify(value, .{}, buf.writer());
    return buf.toOwnedSlice();
}

fn writeMessage(a: Allocator, out: anytype, value: Value) !void {
    var buf = std.ArrayList(u8).init(a);
    defer buf.deinit();
    try std.json.stringify(value, .{}, buf.writer());
    try buf.append('\n');
    try out.writeAll(buf.items);
}

fn sendResult(a: Allocator, out: anytype, id: Value, result: Value) !void {
    var obj = ObjectMap.init(a);
    try obj.put("jsonrpc", Value{ .string = "2.0" });
    try obj.put("id", id);
    try obj.put("result", result);
    try writeMessage(a, out, Value{ .object = obj });
}

fn sendError(a: Allocator, out: anytype, id: Value, code: i64, message: []const u8) !void {
    var err_obj = ObjectMap.init(a);
    try err_obj.put("code", Value{ .integer = code });
    try err_obj.put("message", Value{ .string = message });

    var obj = ObjectMap.init(a);
    try obj.put("jsonrpc", Value{ .string = "2.0" });
    try obj.put("id", id);
    try obj.put("error", Value{ .object = err_obj });
    try writeMessage(a, out, Value{ .object = obj });
}

/// A tool call result: content is a list of content blocks; here always a
/// single text block. `is_error` maps to MCP's `isError` flag so tool-level
/// failures are reported in-band rather than as protocol errors.
fn toolTextResult(a: Allocator, out: anytype, id: Value, text: []const u8, is_error: bool) !void {
    var block = ObjectMap.init(a);
    try block.put("type", Value{ .string = "text" });
    try block.put("text", Value{ .string = text });

    var content = Array.init(a);
    try content.append(Value{ .object = block });

    var result = ObjectMap.init(a);
    try result.put("content", Value{ .array = content });
    try result.put("isError", Value{ .bool = is_error });

    try sendResult(a, out, id, Value{ .object = result });
}

fn log(comptime fmt: []const u8, args: anytype) void {
    std.debug.print("[" ++ SERVER_NAME ++ "] " ++ fmt ++ "\n", args);
}
