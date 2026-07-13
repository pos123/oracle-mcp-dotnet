# AGENTS.md — Oracle Read-Only MCP Server (.NET)

## Build & Test

```bash
dotnet build                    # restore + build (Debug)
dotnet test                     # run all tests (xUnit, no DB needed)
dotnet test -v n                # verbose test output
dotnet test --filter "SqlGuard" # run a single test class
dotnet test --filter "Category=Integration" # integration tests (requires Oracle DB)
dotnet test --filter "Category!=Integration" # unit tests only
dotnet build -c Release         # release build
```

Tests are self-contained — no Oracle database required. ConfigLoader tests use temp dirs + `ORACLE_MCP_CONFIG_PATH` env var (cleaned up in `finally`). SqlGuard tests validate the read-only SQL parser.

## Project Structure

- `src/OracleMcpServer/` — `net8.0` console app, entrypoint `Program.cs`
- `test/OracleMcpServer.Tests/` — xUnit tests, references main project via `ProjectReference`
- `OracleMcpServer.sln` — single solution with both projects

## Framework & Packages

| Package | Version | Note |
|---|---|---|
| `ModelContextProtocol` | 1.4.1 | MCP C# SDK |
| `Oracle.ManagedDataAccess.Core` | 23.7.0 | Thin mode, no OCI/Instant Client |
| `Microsoft.Extensions.Hosting` | 8.0.1 | DI, logging, lifecycle |
| xUnit | 2.9.3 | Test framework |

## Key Quirks

- **Config path resolution**: `ORACLE_MCP_CONFIG_PATH` env var > `appsettings.jsonc` in CWD. File is JSONC (comments stripped manually by `ConfigLoader`).
- **`appsettings.jsonc` is gitignored** — copy from `src/OracleMcpServer/appsettings.example.jsonc` to get started.
- **All logging to stderr** — MCP stdio protocol uses stdout for JSON-RPC messages; `ILogger` output goes to stderr.
- **`OracleClient` instances are cached** per environment in a static `ConcurrentDictionary`. Call `OracleClient.ResetCache()` in tests or after config changes.
- **Environment names are normalized to UPPERCASE** — `ResolveEnvironment("dev1")` matches `"DEV1"` in config.
- **TNS mode requires `tnsnamesPath` to exist on disk** — `ConfigLoader` validates file existence at load time.
- **SQL Guard** blocks DML/DDL/PL-SQL outside string literals and comments. Multi-statement input (semicolons outside strings) is rejected. `SELECT ... FOR UPDATE` is blocked.
- **`RunQuery` auto-wraps** with `SELECT * FROM ({sql}) WHERE ROWNUM <= N+1` and caps at `fetchMaxRows`.
- **Self-contained publish**: `dotnet publish src/OracleMcpServer/OracleMcpServer.csproj -r linux-x64 --self-contained true -o ./publish`

## Architecture

```
Program.cs (Host.CreateApplicationBuilder)
  ├── AddMcpServer() + WithStdioServerTransport() + WithToolsFromAssembly()
  ├── AddSingleton<ConfigLoader>()
  └── Loads config on startup, exits with error on ConfigError

OracleTools (static, [McpServerToolType]) — 11 MCP tools
  └── OracleClient (per-env, ConcurrentDictionary-cached)
       └── Oracle.ManagedDataAccess (thin mode)

SqlGuard (static) — parse + validate read-only SQL
ConfigLoader (singleton) — JSONC parsing, env resolution
```

## Deployment

```bash
# Publish self-contained for target platform
dotnet publish -r linux-x64 --self-contained true -o ./publish
# Optional single-file
dotnet publish -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish-single
```

The MCP client launches the server binary as a subprocess (stdio transport). Set `ORACLE_MCP_CONFIG_PATH` in the client's `environment` block.