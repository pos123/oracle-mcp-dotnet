using System.Text.Json;
using System.Text.Json.Serialization;
using OracleMcpServer.Models;

namespace OracleMcpServer.Config;

public sealed class ConfigError : Exception
{
    public ConfigError(string message) : base(message) { }
    public ConfigError(string message, Exception inner) : base(message, inner) { }
}

public sealed class ConfigLoader
{
    private const string DefaultConfigPath = "appsettings.jsonc";
    private const string EnvVarName = "ORACLE_MCP_CONFIG_PATH";

    private OracleConfigRegistry? _registry;

    private static string StripJsoncComments(string text)
    {
        var cleaned = new char[text.Length];
        var i = 0;
        var length = text.Length;
        var state = "normal";

        while (i < length)
        {
            var ch = text[i];
            var next = i + 1 < length ? text[i + 1] : '\0';

            switch (state)
            {
                case "normal":
                    if (ch == '"')
                    {
                        cleaned[i] = ch;
                        state = "string";
                    }
                    else if (ch == '/' && next == '/')
                    {
                        cleaned[i] = ' ';
                        cleaned[i + 1] = ' ';
                        i++;
                        state = "line_comment";
                    }
                    else if (ch == '/' && next == '*')
                    {
                        cleaned[i] = ' ';
                        cleaned[i + 1] = ' ';
                        i++;
                        state = "block_comment";
                    }
                    else
                    {
                        cleaned[i] = ch;
                    }
                    break;

                case "string":
                    cleaned[i] = ch;
                    if (ch == '\\' && i + 1 < length)
                    {
                        i++;
                        cleaned[i] = text[i];
                    }
                    else if (ch == '"')
                    {
                        state = "normal";
                    }
                    break;

                case "line_comment":
                    if (ch == '\n')
                    {
                        cleaned[i] = ch;
                        state = "normal";
                    }
                    else
                    {
                        cleaned[i] = ' ';
                    }
                    break;

                case "block_comment":
                    if (ch == '*' && next == '/')
                    {
                        cleaned[i] = ' ';
                        cleaned[i + 1] = ' ';
                        i++;
                        state = "normal";
                    }
                    else
                    {
                        cleaned[i] = ' ';
                    }
                    break;
            }
            i++;
        }

        return new string(cleaned);
    }

    private static string GetConfigPath()
    {
        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        return !string.IsNullOrWhiteSpace(envPath) ? envPath.Trim() : DefaultConfigPath;
    }

    private sealed record RawEnvironmentConfig
    {
        public string? Mode { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public int? FetchMaxRows { get; init; }
        public string? DefaultSchema { get; init; }
        public int? ConnectTimeout { get; init; }
        public string? Protocol { get; init; }
        public string? TnsnamesPath { get; init; }
        public string? DsnAlias { get; init; }
        public string? Host { get; init; }
        public int? Port { get; init; }
        public string? ServiceName { get; init; }
    }

    private sealed record RawConfig
    {
        public Dictionary<string, int>? Defaults { get; init; }
        public Dictionary<string, RawEnvironmentConfig>? Environments { get; init; }
    }

    private static OracleConfig BuildEnvironmentConfig(
        string name, RawEnvironmentConfig raw, int defaultFetchMaxRows, int defaultConnectTimeout, string defaultProtocol)
    {
        var mode = raw.Mode?.Trim().ToLowerInvariant() switch
        {
            "tns" => ConnectionMode.Tns,
            "direct" => ConnectionMode.Direct,
            _ => throw new ConfigError($"Environment '{name}': 'mode' must be 'tns' or 'direct'")
        };

        var username = raw.Username?.Trim() ?? throw new ConfigError($"Environment '{name}': 'username' is required");
        var password = raw.Password?.Trim() ?? throw new ConfigError($"Environment '{name}': 'password' is required");
        var fetchMaxRows = raw.FetchMaxRows ?? defaultFetchMaxRows;
        var connectTimeout = raw.ConnectTimeout ?? defaultConnectTimeout;
        var protocol = (!string.IsNullOrWhiteSpace(raw.Protocol) ? raw.Protocol.Trim() : defaultProtocol);

        if (fetchMaxRows <= 0)
            throw new ConfigError($"Environment '{name}': 'fetchMaxRows' must be positive");
        if (connectTimeout <= 0)
            throw new ConfigError($"Environment '{name}': 'connectTimeout' must be positive");

        if (mode == ConnectionMode.Tns)
        {
            var tnsPath = raw.TnsnamesPath?.Trim() ?? throw new ConfigError($"Environment '{name}': 'tnsnamesPath' is required in TNS mode");
            if (!File.Exists(tnsPath))
                throw new ConfigError($"Environment '{name}': 'tnsnamesPath' file not found: {tnsPath}");
            var dsnAlias = raw.DsnAlias?.Trim() ?? throw new ConfigError($"Environment '{name}': 'dsnAlias' is required in TNS mode");

            return new OracleConfig
            {
                Name = name,
                Mode = mode,
                Username = username,
                Password = password,
                FetchMaxRows = fetchMaxRows,
                DefaultSchema = !string.IsNullOrWhiteSpace(raw.DefaultSchema) ? raw.DefaultSchema.Trim() : null,
                ConnectTimeout = connectTimeout,
                Protocol = protocol,
                TnsnamesPath = tnsPath,
                DsnAlias = dsnAlias
            };
        }
        else
        {
            var host = raw.Host?.Trim() ?? throw new ConfigError($"Environment '{name}': 'host' is required in direct mode");
            var port = raw.Port ?? throw new ConfigError($"Environment '{name}': 'port' is required in direct mode");
            if (port <= 0)
                throw new ConfigError($"Environment '{name}': 'port' must be positive");
            var serviceName = raw.ServiceName?.Trim() ?? throw new ConfigError($"Environment '{name}': 'serviceName' is required in direct mode");

            return new OracleConfig
            {
                Name = name,
                Mode = mode,
                Username = username,
                Password = password,
                FetchMaxRows = fetchMaxRows,
                DefaultSchema = !string.IsNullOrWhiteSpace(raw.DefaultSchema) ? raw.DefaultSchema.Trim() : null,
                ConnectTimeout = connectTimeout,
                Protocol = protocol,
                Host = host,
                Port = port,
                ServiceName = serviceName
            };
        }
    }

    public OracleConfigRegistry Load()
    {
        if (_registry is not null)
            return _registry;

        var configPath = GetConfigPath();

        string rawText;
        try
        {
            rawText = File.ReadAllText(configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigError($"Unable to read config file: {configPath}", ex);
        }

        var json = StripJsoncComments(rawText);

        RawConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RawConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigError($"Invalid JSONC in config file: {configPath}", ex);
        }

        if (parsed is null)
            throw new ConfigError("Config file root must be an object");

        var environments = parsed.Environments ?? throw new ConfigError("'environments' must be a non-empty object");
        if (environments.Count == 0)
            throw new ConfigError("'environments' must be a non-empty object");

        var defaults = parsed.Defaults ?? [];
        var defaultFetchMaxRows = defaults.GetValueOrDefault("fetchMaxRows", 500);
        var defaultConnectTimeout = defaults.GetValueOrDefault("connectTimeout", 15);

        var loaded = new Dictionary<string, OracleConfig>();
        foreach (var (rawName, rawConfig) in environments)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                throw new ConfigError("Environment names must be non-empty strings");
            if (rawConfig is null)
                throw new ConfigError($"Environment '{rawName}' must be an object");

            var envName = rawName.Trim().ToUpperInvariant();
            if (loaded.ContainsKey(envName))
                throw new ConfigError($"Duplicate environment name after normalization: {envName}");

            var defaultProtocol = defaults.GetValueOrDefault("protocol", 0) == 0
                ? "tcp"
                : defaults.GetValueOrDefault("protocol", 0).ToString();

            loaded[envName] = BuildEnvironmentConfig(envName, rawConfig, defaultFetchMaxRows, defaultConnectTimeout, "tcp");
        }

        _registry = new OracleConfigRegistry
        {
            ConfigPath = configPath,
            Environments = loaded
        };

        return _registry;
    }

    public OracleConfig ResolveEnvironment(string environment)
    {
        var registry = Load();
        var key = environment.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(key))
            throw new ConfigError("Environment name cannot be empty");

        if (registry.Environments.TryGetValue(key, out var config))
            return config;

        var available = string.Join(", ", registry.Environments.Keys.OrderBy(k => k));
        throw new ConfigError($"Unknown environment '{environment}'. Available environments: {available}");
    }
}