# Oracle Read-Only MCP Server (.NET)

.NET Core MCP server for Oracle database read-only access using `Oracle.ManagedDataAccess.Core` (thin mode, no OCI / Instant Client required).

This is a direct port of the [Python `oracle-mcp`](https://github.com/nadeem/oracle-mcp) server, reimplemented in .NET for self-contained deployment scenarios.

## Why .NET?

- **Self-contained publish** — produce a single folder with the executable and all dependencies, no runtime installation required
- **Single-file executable** — optional `PublishSingleFile` produces a single binary
- **Cross-platform** — runs on Linux, macOS, and Windows
- **Managed Oracle driver** — `Oracle.ManagedDataAccess.Core` connects in thin mode, same as python-oracledb

## Features

All 11 MCP tools, identical to the Python version:

| Tool | Description |
|---|---|
| `list_environments()` | List configured environment monikers and connection mode |
| `check_database(environment)` | Verify environment availability (real `SELECT 1 FROM dual` connection) |
| `run_query(environment, sql)` | Execute a single read-only `SELECT` or `WITH ... SELECT` query |
| `preview_query(sql)` | Validate SQL with the same read-only guard, return normalized SQL without executing |
| `list_tables(environment, schema?, name_pattern?)` | List accessible tables |
| `list_views(environment, schema?, name_pattern?)` | List accessible views |
| `search_objects(environment, name_pattern, schema?, object_types?)` | Search objects by SQL `LIKE` pattern |
| `describe_table(environment, table_name, schema?)` | Full table/view structure with types, precision, defaults, comments |
| `list_columns(environment, table_name, schema?)` | Lightweight column listing |
| `get_primary_key(environment, table_name, schema?)` | Primary key columns and position order |
| `list_foreign_keys(environment, table_name, schema?)` | Outbound foreign key relationships and referenced columns |
| `get_table_sample(environment, table_name, schema?, limit?)` | Capped sample of rows from a table or view |
| `list_schemas(environment)` | List visible Oracle schemas/users |

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- Oracle database reachable over the network
- No OCI / Oracle Instant Client required

## Quick Start

```bash
# Clone or copy the project
cd oracle-mcp-dotnet

# Copy the example config and fill in your credentials
cp src/OracleMcpServer/appsettings.example.jsonc src/OracleMcpServer/appsettings.jsonc

# Run in development mode
dotnet run --project src/OracleMcpServer
```

## Configuration

The server uses a single JSONC configuration file. Unlike the Python version which uses `oracle-mcp.jsonc`, this .NET version uses `appsettings.jsonc` (more conventional for .NET projects).

The config file path is resolved in this order:

1. `ORACLE_MCP_CONFIG_PATH` environment variable
2. `appsettings.jsonc` in the current working directory

### Config Structure

```jsonc
{
  // Shared defaults for all environments
  "defaults": {
    "fetchMaxRows": 500,
    "connectTimeout": 15
  },

  "environments": {
    "DEV1": {
      "mode": "tns",
      "tnsnamesPath": "C:\\oracle\\network\\admin\\tnsnames.ora",
      "dsnAlias": "DEV1",
      "username": "readonly_user",
      "password": "plain_text_password",
      "defaultSchema": "APP"   // optional
    },
    "UAT3": {
      "mode": "direct",
      "host": "uat3-db.internal",
      "port": 1521,
      "serviceName": "UAT3",
      "username": "readonly_user",
      "password": "plain_text_password",
      "defaultSchema": "APP"   // optional
    }
  }
}
```

### Schema Resolution Order

For metadata tools (`list_tables`, `describe_table`, `list_columns`, etc.), the schema/owner is resolved in this priority:

1. Explicit `schema` argument passed to the tool
2. `defaultSchema` from the environment config
3. The Oracle username for that environment

## Self-Contained Deployment

This is the primary reason for the .NET port — produce a standalone executable that does not require the .NET runtime to be installed on the target machine.

```bash
# Publish for Linux x64 (self-contained)
dotnet publish src/OracleMcpServer/OracleMcpServer.csproj \
  -r linux-x64 \
  --self-contained true \
  -o ./publish

# Run it
./publish/OracleMcpServer
```

### Other Runtimes

```bash
# Windows x64
dotnet publish -r win-x64 --self-contained true -o ./publish-win

# macOS x64
dotnet publish -r osx-x64 --self-contained true -o ./publish-mac

# macOS Apple Silicon
dotnet publish -r osx-arm64 --self-contained true -o ./publish-mac-arm
```

### Single-File Executable (optional)

```bash
dotnet publish -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish-single
```

## OpenCode MCP Configuration

In your `opencode.json` or `opencode.jsonc`, add an entry under the `mcp` key:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "oracle_readonly": {
      "type": "local",
      "command": ["/absolute/path/to/oracle-mcp-dotnet/publish/OracleMcpServer"],
      "cwd": "/absolute/path/to/oracle-mcp-dotnet",
      "enabled": true,
      "environment": {
        "ORACLE_MCP_CONFIG_PATH": "/absolute/path/to/appsettings.jsonc"
      }
    }
  }
}
```

For development mode (requires .NET SDK on the target machine):

```jsonc
{
  "oracle_readonly": {
    "type": "local",
    "command": ["dotnet", "run", "--project", "/absolute/path/to/oracle-mcp-dotnet/src/OracleMcpServer"],
    "cwd": "/absolute/path/to/oracle-mcp-dotnet",
    "enabled": true,
    "environment": {
      "ORACLE_MCP_CONFIG_PATH": "/absolute/path/to/appsettings.jsonc"
    }
  }
}
```

## Generic MCP Client Configuration

```json
{
  "mcpServers": {
    "oracle-readonly": {
      "command": "/absolute/path/to/oracle-mcp-dotnet/publish/OracleMcpServer",
      "env": {
        "ORACLE_MCP_CONFIG_PATH": "/absolute/path/to/appsettings.jsonc"
      }
    }
  }
}
```

## Typical Workflow

Once configured in your MCP client (e.g. OpenCode):

1. `list_environments()` — see what's available
2. `check_database("DEV1")` — confirm connectivity
3. `run_query("DEV1", "select * from all_tables where rownum <= 5")` — query data
4. `list_tables("DEV1", namePattern: "EMP%")` — explore schema
5. `describe_table("DEV1", "EMPLOYEES")` — inspect table structure

## Security Model

The server enforces read-only SQL at the application level, but the real security boundary is the Oracle account itself.

**Application-level protection:**

- Only read-oriented MCP tools are exposed
- `run_query()` and `preview_query()` validate SQL before execution
- Multi-statement input is rejected
- DML, DDL, and PL/SQL keywords are blocked outside string literals
- `SELECT ... FOR UPDATE` is also blocked

**Database-level protection (recommended):**

- The Oracle user should only have `CREATE SESSION` and `SELECT` privileges
- Do not use `SYSTEM`, `SYS`, or any account with write privileges
- The Oracle account should not have `EXECUTE` on dangerous packages such as `DBMS_SQL`, `UTL_FILE`, or `DBMS_LOB`

## Project Structure

```
oracle-mcp-dotnet/
├── OracleMcpServer.sln
├── src/
│   └── OracleMcpServer/
│       ├── OracleMcpServer.csproj   # NuGet references, net8.0
│       ├── Program.cs               # Entry point, DI setup, MCP server
│       ├── appsettings.example.jsonc # Config template
│       ├── Models/
│       │   └── OracleConfig.cs      # OracleConfig, QueryResult records
│       ├── Config/
│       │   └── ConfigLoader.cs      # JSONC parsing, env resolution, caching
│       ├── Guards/
│       │   └── SqlGuard.cs          # Read-only SQL validation
│       ├── Services/
│       │   └── OracleClient.cs      # Oracle connection + all DB operations
│       └── Tools/
│           └── OracleTools.cs       # [McpServerToolType] class with 11 tools
└── test/
    └── OracleMcpServer.Tests/
        ├── OracleMcpServer.Tests.csproj
        ├── ConfigLoaderTests.cs      # 6 tests
        ├── SqlGuardTests.cs          # 20 tests
        └── OracleClientIntegrationTests.cs  # 11 tests (skipped by default)
```

## Architecture

```
┌─────────────────────────────────────────────────┐
│  MCP Client (e.g. OpenCode)                     │
│  sends JSON-RPC over stdin/stdout               │
└──────────────┬──────────────────────────────────┘
               │ stdio
┌──────────────▼──────────────────────────────────┐
│  Program.cs                                     │
│  ┌───────────────────────────────────────────┐  │
│  │  Host.CreateApplicationBuilder            │  │
│  │  ├── AddMcpServer()                       │  │
│  │  │   └── WithStdioServerTransport()       │  │
│  │  │   └── WithToolsFromAssembly()          │  │
│  │  └── AddSingleton<ConfigLoader>()         │  │
│  └───────────────────────────────────────────┘  │
└──────────────┬──────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────┐
│  OracleTools (static, [McpServerToolType])      │
│  ┌───────────────────────────────────────────┐  │
│  │  [McpServerTool]                          │  │
│  │  public static string RunQuery(...)       │  │
│  │  public static string ListTables(...)     │  │
│  │  ... (11 tools)                           │  │
│  └───────────────────────────────────────────┘  │
└──────────────┬──────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────┐
│  OracleClient (per-environment, cached)         │
│  ┌───────────────────────────────────────────┐  │
│  │  OracleConnection (ManagedDataAccess)     │  │
│  │  ├── check_database                       │  │
│  │  ├── run_query                            │  │
│  │  ├── list_tables / list_views             │  │
│  │  ├── search_objects                       │  │
│  │  ├── describe_table / list_columns        │  │
│  │  ├── get_primary_key / list_foreign_keys  │  │
│  │  ├── get_table_sample                     │  │
│  │  └── list_schemas                         │  │
│  └───────────────────────────────────────────┘  │
└──────────────┬──────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────┐
│  Oracle Database                                 │
└─────────────────────────────────────────────────┘
```

Key design points:

- **Lazy client caching** — `OracleClient` instances are cached per environment name in a `ConcurrentDictionary`, created on first use
- **DI** — `ConfigLoader` is registered as a singleton and injected into tools automatically
- **All logging to stderr** — MCP stdio protocol uses stdout for JSON-RPC messages; all `ILogger` output goes to stderr
- **Config pre-validation** — `Program.cs` loads the config on startup and exits immediately with an error message if it's invalid

## Building

```bash
# Restore and build
dotnet build

# Build in Release mode
dotnet build -c Release

# Run tests
dotnet test

# Run tests with verbose output
dotnet test -v n
```

## Testing

```bash
dotnet test
```

The test suite covers:

- **ConfigLoader** (6 tests): valid config parsing, JSONC comment stripping, missing file, invalid mode, empty environments, unknown environment rejection
- **SqlGuard** (20 tests): allowed SELECT/WITH, empty SQL, blocked INSERT/UPDATE/DELETE/DROP/CREATE/ALTER/MERGE/TRUNCATE, multi-statement rejection, `SELECT FOR UPDATE` blocking, keyword-in-string-literal allowance, keyword-in-comment allowance, case-insensitive block, comment stripping, whitespace normalization

### Integration Tests (11 tests, skipped by default)

Tests that exercise `OracleClient` against a real database. All are gated behind `[Fact(Skip=...)]` and are skipped in normal `dotnet test` runs.

To run them, start a local Oracle XE container:

```bash
docker run -d --name oracle-xe -p 1521:1521 -e ORACLE_PASSWORD=oracle gvenzl/oracle-xe
```

Then run the tests (remove the `Skip` attribute or use an explicit filter):

```bash
dotnet test --filter "OracleClientIntegration"
```

The tests connect to `localhost:1521/XEPDB1` as `APP`/`oracle` and cover:
`CheckDatabase`, `RunQuery` (simple + truncated), `ListTables`, `ListViews`, `ListSchemas`, `DescribeTable`, `ListColumns`, `GetPrimaryKey`, `SearchObjects`, `PreviewQuery`.

## NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 1.4.1 | Microsoft's official MCP C# SDK |
| `Oracle.ManagedDataAccess.Core` | 23.7.0 | Managed Oracle driver (thin mode, no OCI) |
| `Microsoft.Extensions.Hosting` | 8.0.1 | DI, logging, application lifecycle |

