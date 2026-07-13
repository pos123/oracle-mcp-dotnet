namespace OracleMcpServer.Models;

public enum ConnectionMode
{
    Tns,
    Direct
}

public sealed record OracleConfig
{
    public required string Name { get; init; }
    public required ConnectionMode Mode { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public int FetchMaxRows { get; init; } = 500;
    public string? DefaultSchema { get; init; }
    public int ConnectTimeout { get; init; } = 15;
    public string Protocol { get; init; } = "tcp";

    public string? TnsnamesPath { get; init; }
    public string? DsnAlias { get; init; }

    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ServiceName { get; init; }
}

public sealed record OracleConfigRegistry
{
    public required string ConfigPath { get; init; }
    public required Dictionary<string, OracleConfig> Environments { get; init; }
}

public sealed record QueryResult
{
    public required List<string> Columns { get; init; }
    public required List<List<object?>> Rows { get; init; }
    public int RowCount { get; init; }
    public bool Truncated { get; init; }
}