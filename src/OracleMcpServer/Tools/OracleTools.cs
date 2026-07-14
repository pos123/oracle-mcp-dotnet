using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using OracleMcpServer.Config;
using OracleMcpServer.Guards;
using OracleMcpServer.Services;
using Serilog;

namespace OracleMcpServer.Tools;

[McpServerToolType]
public static class OracleTools
{
    private static readonly ILogger _logger = Log.ForContext(typeof(OracleTools));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static string ToJson(object payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static OracleClient GetClient(ConfigLoader loader, string environment)
    {
        return OracleClient.GetClient(loader, environment);
    }

    [McpServerTool, Description("List configured Oracle environment monikers.")]
    public static string ListEnvironments(ConfigLoader loader)
    {
        _logger.Information("ListEnvironments");
        var registry = loader.Load();
        var result = registry.Environments
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new
            {
                environment = kvp.Key,
                mode = kvp.Value.Mode.ToString().ToLowerInvariant()
            })
            .ToList();
        return ToJson(result);
    }

    [McpServerTool, Description("Check whether the target Oracle environment is available.")]
    public static string CheckDatabase(ConfigLoader loader, string environment)
    {
        _logger.Information("CheckDatabase: Environment={Environment}", environment);
        return ToJson(GetClient(loader, environment).CheckDatabase());
    }

    [McpServerTool, Description("Run a single read-only Oracle SQL query.")]
    public static string RunQuery(ConfigLoader loader, string environment, string sql)
    {
        _logger.Information("RunQuery: Environment={Environment}, SqlLength={SqlLength}", environment, sql.Length);
        var normalizedSql = SqlGuard.ValidateReadOnlySql(sql);
        var result = GetClient(loader, environment).RunQuery(normalizedSql);
        _logger.Debug("RunQuery result: RowCount={RowCount}, Truncated={Truncated}", result.RowCount, result.Truncated);
        return ToJson(new
        {
            columns = result.Columns,
            rows = result.Rows,
            rowCount = result.RowCount,
            truncated = result.Truncated
        });
    }

    [McpServerTool, Description("Validate and normalize a read-only Oracle SQL query without executing it.")]
    public static string PreviewQuery(string sql)
    {
        _logger.Information("PreviewQuery: SqlLength={SqlLength}", sql.Length);
        var normalizedSql = SqlGuard.ValidateReadOnlySql(sql);
        return ToJson(new
        {
            valid = true,
            normalizedSql
        });
    }

    [McpServerTool, Description("List accessible tables.")]
    public static string ListTables(ConfigLoader loader, string environment, string? schema = null, string? namePattern = null)
    {
        _logger.Information("ListTables: Environment={Environment}, Schema={Schema}, NamePattern={NamePattern}", environment, schema, namePattern);
        return ToJson(GetClient(loader, environment).ListTables(schema, namePattern, includeViews: false));
    }

    [McpServerTool, Description("List accessible views.")]
    public static string ListViews(ConfigLoader loader, string environment, string? schema = null, string? namePattern = null)
    {
        _logger.Information("ListViews: Environment={Environment}, Schema={Schema}, NamePattern={NamePattern}", environment, schema, namePattern);
        var tablesAndViews = GetClient(loader, environment).ListTables(schema, namePattern, includeViews: true);
        return ToJson(tablesAndViews.Where(item => item["type"] == "VIEW"));
    }

    [McpServerTool, Description("Search Oracle objects by name pattern.")]
    public static string SearchObjects(
        ConfigLoader loader,
        string environment,
        string namePattern,
        string? schema = null,
        List<string>? objectTypes = null)
    {
        _logger.Information("SearchObjects: Environment={Environment}, NamePattern={NamePattern}, Schema={Schema}", environment, namePattern, schema);
        return ToJson(GetClient(loader, environment).SearchObjects(namePattern, schema, objectTypes));
    }

    [McpServerTool, Description("Describe a table or view structure.")]
    public static string DescribeTable(ConfigLoader loader, string environment, string tableName, string? schema = null)
    {
        _logger.Information("DescribeTable: Environment={Environment}, TableName={TableName}, Schema={Schema}", environment, tableName, schema);
        return ToJson(GetClient(loader, environment).DescribeTable(schema, tableName));
    }

    [McpServerTool, Description("List columns for a table or view.")]
    public static string ListColumns(ConfigLoader loader, string environment, string tableName, string? schema = null)
    {
        _logger.Information("ListColumns: Environment={Environment}, TableName={TableName}, Schema={Schema}", environment, tableName, schema);
        return ToJson(GetClient(loader, environment).ListColumns(schema, tableName));
    }

    [McpServerTool, Description("List primary key columns for a table.")]
    public static string GetPrimaryKey(ConfigLoader loader, string environment, string tableName, string? schema = null)
    {
        _logger.Information("GetPrimaryKey: Environment={Environment}, TableName={TableName}, Schema={Schema}", environment, tableName, schema);
        return ToJson(GetClient(loader, environment).GetPrimaryKey(schema, tableName));
    }

    [McpServerTool, Description("List foreign key relationships for a table.")]
    public static string ListForeignKeys(ConfigLoader loader, string environment, string tableName, string? schema = null)
    {
        _logger.Information("ListForeignKeys: Environment={Environment}, TableName={TableName}, Schema={Schema}", environment, tableName, schema);
        return ToJson(GetClient(loader, environment).ListForeignKeys(schema, tableName));
    }

    [McpServerTool, Description("Fetch a sample of rows from a table or view.")]
    public static string GetTableSample(
        ConfigLoader loader,
        string environment,
        string tableName,
        string? schema = null,
        int limit = 20)
    {
        _logger.Information("GetTableSample: Environment={Environment}, TableName={TableName}, Schema={Schema}, Limit={Limit}", environment, tableName, schema, limit);
        var safeLimit = Math.Max(limit, 1);
        var result = GetClient(loader, environment).GetTableSample(schema, tableName, safeLimit);
        _logger.Debug("GetTableSample result: RowCount={RowCount}", result.RowCount);
        return ToJson(new
        {
            columns = result.Columns,
            rows = result.Rows,
            rowCount = result.RowCount,
            truncated = result.Truncated
        });
    }

    [McpServerTool, Description("List visible Oracle schemas/users.")]
    public static string ListSchemas(ConfigLoader loader, string environment)
    {
        _logger.Information("ListSchemas: Environment={Environment}", environment);
        return ToJson(GetClient(loader, environment).ListSchemas());
    }
}