using OracleMcpServer.Config;
using OracleMcpServer.Models;
using Xunit;

namespace OracleMcpServer.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_ValidConfig_ParsesEnvironments()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            // Write a tnsnames.ora file referenced by the TNS environment
            File.WriteAllText(Path.Combine(dir, "tnsnames.ora"), "DEV1 = ...");

            var configPath = Path.Combine(dir, "appsettings.jsonc");
            File.WriteAllText(configPath, """
                {
                    "defaults": {
                        "fetchMaxRows": 200,
                        "connectTimeout": 30
                    },
                    "environments": {
                        "DEV1": {
                            "mode": "tns",
                            "tnsnamesPath": "REPLACE_TNS_PATH",
                            "dsnAlias": "DEV1",
                            "username": "user1",
                            "password": "pass1",
                            "defaultSchema": "APP"
                        },
                        "UAT3": {
                            "mode": "direct",
                            "host": "uat3.example.com",
                            "port": 1521,
                            "serviceName": "UAT3",
                            "username": "user2",
                            "password": "pass2",
                            "defaultSchema": "APP"
                        }
                    }
                }
                """.Replace("REPLACE_TNS_PATH", Path.Combine(dir, "tnsnames.ora").Replace("\\", "\\\\")));

            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", configPath);
            var loader = new ConfigLoader();
            var registry = loader.Load();

            Assert.Equal(2, registry.Environments.Count);

            Assert.True(registry.Environments.ContainsKey("DEV1"));
            var dev1 = registry.Environments["DEV1"];
            Assert.Equal("DEV1", dev1.Name);
            Assert.Equal(ConnectionMode.Tns, dev1.Mode);
            Assert.Equal("user1", dev1.Username);
            Assert.Equal("pass1", dev1.Password);
            Assert.Equal(200, dev1.FetchMaxRows);
            Assert.Equal(30, dev1.ConnectTimeout);
            Assert.Equal("APP", dev1.DefaultSchema);
            Assert.Equal(Path.Combine(dir, "tnsnames.ora"), dev1.TnsnamesPath);
            Assert.Equal("DEV1", dev1.DsnAlias);

            Assert.True(registry.Environments.ContainsKey("UAT3"));
            var uat3 = registry.Environments["UAT3"];
            Assert.Equal("UAT3", uat3.Name);
            Assert.Equal(ConnectionMode.Direct, uat3.Mode);
            Assert.Equal("user2", uat3.Username);
            Assert.Equal("pass2", uat3.Password);
            Assert.Equal("uat3.example.com", uat3.Host);
            Assert.Equal(1521, uat3.Port);
            Assert.Equal("UAT3", uat3.ServiceName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ConfigWithComments_StripsComments()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "appsettings.jsonc");
            File.WriteAllText(configPath, """
                {
                    // This is a comment
                    "defaults": {
                        "fetchMaxRows": 100 /* inline block comment */
                    },
                    "environments": {
                        "TEST": {
                            "mode": "direct",
                            "host": "host.example.com",
                            "port": 1521,
                            "serviceName": "XE",
                            "username": "user",
                            "password": "pass"
                        }
                    }
                }
                """);

            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", configPath);
            var loader = new ConfigLoader();
            var registry = loader.Load();

            Assert.Single(registry.Environments);
            Assert.Equal(100, registry.Environments["TEST"].FetchMaxRows);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        var loader = new ConfigLoader();
        Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", "/nonexistent/path.jsonc");
        try
        {
            Assert.Throws<ConfigError>(() => loader.Load());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
        }
    }

    [Fact]
    public void Load_InvalidMode_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "appsettings.jsonc");
            File.WriteAllText(configPath, """
                {
                    "environments": {
                        "BAD": {
                            "mode": "invalid",
                            "username": "u",
                            "password": "p"
                        }
                    }
                }
                """);

            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", configPath);
            var loader = new ConfigLoader();
            Assert.Throws<ConfigError>(() => loader.Load());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_NoEnvironments_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "appsettings.jsonc");
            File.WriteAllText(configPath, "{}");

            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", configPath);
            var loader = new ConfigLoader();
            Assert.Throws<ConfigError>(() => loader.Load());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResolveEnvironment_Known_ReturnsConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "appsettings.jsonc");
            File.WriteAllText(configPath, """
                {
                    "environments": {
                        "MYENV": {
                            "mode": "direct",
                            "host": "h",
                            "port": 1521,
                            "serviceName": "SVC",
                            "username": "u",
                            "password": "p"
                        }
                    }
                }
                """);

            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", configPath);
            var loader = new ConfigLoader();
            var config = loader.ResolveEnvironment("myenv");

            Assert.Equal("MYENV", config.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResolveEnvironment_Unknown_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "appsettings.jsonc");
            File.WriteAllText(configPath, """
                {
                    "environments": {
                        "EXISTING": {
                            "mode": "direct",
                            "host": "h",
                            "port": 1521,
                            "serviceName": "SVC",
                            "username": "u",
                            "password": "p"
                        }
                    }
                }
                """);

            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", configPath);
            var loader = new ConfigLoader();
            Assert.Throws<ConfigError>(() => loader.ResolveEnvironment("NONEXISTENT"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORACLE_MCP_CONFIG_PATH", null);
            Directory.Delete(dir, true);
        }
    }
}