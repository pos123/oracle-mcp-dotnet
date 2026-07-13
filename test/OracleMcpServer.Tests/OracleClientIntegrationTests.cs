using OracleMcpServer.Config;
using OracleMcpServer.Guards;
using OracleMcpServer.Services;
using OracleMcpServer.Tools;
using Xunit;

namespace OracleMcpServer.Tests;

[Collection("OracleClientIntegration")]
public sealed class OracleClientIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _previousEnvValue;

    private const string LocalServiceName = "XEPDB1";
    private const string LocalUsername = "APP";
    private const string LocalPassword = "oracle";

    public OracleClientIntegrationTests()
    {
        OracleClient.ResetCache();

        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        _previousEnvValue = Environment.GetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH") ?? "";

        _configPath = Path.Combine(_tempDir, "appsettings.jsonc");
        File.WriteAllText(_configPath, $$"""
            {
                "defaults": {
                    "fetchMaxRows": 100,
                    "connectTimeout": 15
                },
                "environments": {
                    "LOCAL": {
                        "mode": "direct",
                        "host": "localhost",
                        "port": 1521,
                        "serviceName": "{{LocalServiceName}}",
                        "username": "{{LocalUsername}}",
                        "password": "{{LocalPassword}}"
                    }
                }
            }
            """);

        Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", _configPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH",
            string.IsNullOrEmpty(_previousEnvValue) ? null : _previousEnvValue);
        try { Directory.Delete(_tempDir, true); } catch { }
        OracleClient.ResetCache();
    }

    private static OracleClient GetClient()
    {
        var loader = new ConfigLoader();
        return OracleClient.GetClient(loader, "LOCAL");
    }

    [Fact, Trait("Category", "Integration")]
    public void CheckDatabase_ReturnsAvailable()
    {
        var result = GetClient().CheckDatabase();
        Assert.True((bool)result["available"]!);
        Assert.Equal("direct", (string)result["mode"]!);
    }

    [Fact, Trait("Category", "Integration")]
    public void RunQuery_SimpleSelect_ReturnsRows()
    {
        var result = GetClient().RunQuery("SELECT 1 AS val FROM dual");
        Assert.Equal(1, result.RowCount);
        Assert.Contains("VAL", result.Columns);
    }

    [Fact, Trait("Category", "Integration")]
    public void RunQuery_WithLimit_Truncated()
    {
        var result = GetClient().RunQuery("SELECT * FROM all_objects");
        Assert.True(result.Truncated);
        Assert.Equal(100, result.RowCount);
    }

    [Fact, Trait("Category", "Integration")]
    public void ListTables_ReturnsTables()
    {
        var result = GetClient().ListTables("SYS", null);
        Assert.NotEmpty(result);
    }

    [Fact, Trait("Category", "Integration")]
    public void ListViews_ReturnsViews()
    {
        var result = GetClient().ListTables("SYS", null, includeViews: true);
        var views = result.Where(r => r["type"] == "VIEW").ToList();
        Assert.NotEmpty(views);
    }

    [Fact, Trait("Category", "Integration")]
    public void ListSchemas_ContainsApp()
    {
        var result = GetClient().ListSchemas();
        Assert.Contains("APP", result);
    }

    [Fact, Trait("Category", "Integration")]
    public void DescribeTable_SystemTable_ReturnsColumns()
    {
        var result = GetClient().DescribeTable("SYS", "ALL_TABLES");
        Assert.NotEmpty(result);
        Assert.Contains(result, c => (string)c["columnName"]! == "TABLE_NAME");
    }

    [Fact, Trait("Category", "Integration")]
    public void ListColumns_ReturnsColumns()
    {
        var result = GetClient().ListColumns("SYS", "ALL_TABLES");
        Assert.NotEmpty(result);
        Assert.Contains(result, c => (string)c["columnName"]! == "TABLE_NAME");
    }

    [Fact, Trait("Category", "Integration")]
    public void GetPrimaryKey_OnDual_ReturnsEmpty()
    {
        var result = GetClient().GetPrimaryKey("SYS", "DUAL");
        Assert.Empty(result);
    }

    [Fact, Trait("Category", "Integration")]
    public void SearchObjects_FindsTables()
    {
        var result = GetClient().SearchObjects("ALL_%", "SYS", ["TABLE", "VIEW"]);
        Assert.NotEmpty(result);
    }

    [Fact, Trait("Category", "Integration")]
    public void PreviewQuery_Validates()
    {
        var result = OracleTools.PreviewQuery("SELECT 1 FROM dual");
        Assert.Contains("valid", result);
    }
}