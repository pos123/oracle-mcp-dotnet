using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using OracleMcpServer.Config;
using OracleMcpServer.Models;
using Serilog;

namespace OracleMcpServer.Services;

public sealed partial class OracleClient
{
    private static readonly ConcurrentDictionary<string, OracleClient> _instances = new();
    private static readonly ILogger _logger = Log.ForContext(typeof(OracleClient));

    private readonly OracleConfig _config;

    private OracleClient(OracleConfig config)
    {
        _config = config;
    }

    public static OracleClient GetClient(ConfigLoader loader, string environment)
    {
        var config = loader.ResolveEnvironment(environment);
        return _instances.GetOrAdd(config.Name, _ =>
        {
            _logger.Debug("OracleClient cache miss: creating new client for Environment={Environment}", config.Name);
            return new OracleClient(config);
        });
    }

    public static void ResetCache()
    {
        _instances.Clear();
    }

    private string BuildConnectionString()
    {
        var builder = new OracleConnectionStringBuilder
        {
            UserID = _config.Username,
            Password = _config.Password,
            ConnectionTimeout = _config.ConnectTimeout
        };

        if (_config.Mode == ConnectionMode.Tns)
        {
            builder["DSN"] = _config.DsnAlias;
            builder["ConfigDir"] = Path.GetDirectoryName(_config.TnsnamesPath);
        }
        else
        {
            builder["Data Source"] = $"(DESCRIPTION=(ADDRESS=(PROTOCOL={_config.Protocol})(HOST={_config.Host})(PORT={_config.Port}))(CONNECT_DATA=(SERVICE_NAME={_config.ServiceName})))";
        }

        return builder.ConnectionString;
    }

    private string DefaultOwner(string? schema)
    {
        return (schema ?? _config.DefaultSchema ?? _config.Username).ToUpperInvariant();
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9_$#]*")]
    private static partial Regex IdentifierRegex();

    private static string QuoteIdentifier(string identifier)
    {
        if (!IdentifierRegex().IsMatch(identifier))
            throw new ArgumentException("Oracle identifiers must contain only letters, numbers, _, $, or #");
        return $"\"{identifier.ToUpperInvariant()}\"";
    }

    private OracleConnection CreateReadonlyConnection()
    {
        var conn = new OracleConnection(BuildConnectionString());
        conn.Open();
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SET TRANSACTION READ ONLY";
        cmd.ExecuteNonQuery();
        return conn;
    }


    public Dictionary<string, object?> CheckDatabase()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = CreateReadonlyConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM dual";
            cmd.ExecuteScalar();
            sw.Stop();

            return new Dictionary<string, object?>
            {
                ["environment"] = _config.Name,
                ["available"] = true,
                ["mode"] = _config.Mode.ToString().ToLowerInvariant(),
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error(ex, "CheckDatabase failed: Environment={Environment}, DurationMs={DurationMs}",
                _config.Name, Math.Round(sw.Elapsed.TotalMilliseconds, 2));
            return new Dictionary<string, object?>
            {
                ["environment"] = _config.Name,
                ["available"] = false,
                ["mode"] = _config.Mode.ToString().ToLowerInvariant(),
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ["error"] = ex.Message
            };
        }
    }

    public QueryResult RunQuery(string sql)
    {
        var sw = Stopwatch.StartNew();
        var maxRows = _config.FetchMaxRows;
        var limitedSql = $"SELECT * FROM ({sql}) WHERE ROWNUM <= {maxRows + 1}";

        using var conn = CreateReadonlyConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = limitedSql;

        List<string> columns;
        List<List<object?>> rows;

        using var reader = cmd.ExecuteReader();

        columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        rows = new List<List<object?>>();
        while (reader.Read())
        {
            var row = new List<object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                row.Add(val == DBNull.Value ? null : val);
            }
            rows.Add(row);
        }

        var truncated = rows.Count > maxRows;
        if (truncated)
            rows = rows.Take(maxRows).ToList();

        sw.Stop();
        _logger.Debug("RunQuery: Environment={Environment}, RowCount={RowCount}, Truncated={Truncated}, DurationMs={DurationMs}",
            _config.Name, rows.Count, truncated, Math.Round(sw.Elapsed.TotalMilliseconds, 2));

        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            Truncated = truncated
        };
    }

    public List<Dictionary<string, string>> ListObjects(
        string? schema, string? namePattern, List<string> objectTypes)
    {
        var filters = new List<string>
        {
            $"OBJECT_TYPE IN ({string.Join(", ", objectTypes.Select(t => $"'{t.ToUpperInvariant()}'"))})"
        };
        var parameters = new Dictionary<string, object>();

        var owner = DefaultOwner(schema);
        filters.Add("OWNER = :owner");
        parameters["owner"] = owner;

        if (!string.IsNullOrWhiteSpace(namePattern))
        {
            filters.Add("OBJECT_NAME LIKE :namePattern");
            parameters["namePattern"] = namePattern.ToUpperInvariant();
        }

        var sql = $"SELECT OWNER, OBJECT_NAME, OBJECT_TYPE FROM ALL_OBJECTS WHERE {string.Join(" AND ", filters)} ORDER BY OWNER, OBJECT_NAME";

        using var conn = CreateReadonlyConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (key, value) in parameters)
            cmd.Parameters.Add(new OracleParameter(key, value));

        using var reader = cmd.ExecuteReader();
        var results = new List<Dictionary<string, string>>();
        while (reader.Read())
        {
            results.Add(new Dictionary<string, string>
            {
                ["schema"] = reader.GetString(0),
                ["name"] = reader.GetString(1),
                ["type"] = reader.GetString(2)
            });
        }
        return results;
    }

    public List<Dictionary<string, string>> ListTables(string? schema, string? namePattern, bool includeViews = false)
    {
        var types = new List<string> { "TABLE" };
        if (includeViews)
            types.Add("VIEW");
        return ListObjects(schema, namePattern, types);
    }

    public List<Dictionary<string, string>> SearchObjects(
        string namePattern, string? schema, List<string>? objectTypes)
    {
        return ListObjects(schema, namePattern, objectTypes ?? ["TABLE", "VIEW", "SYNONYM"]);
    }

    public List<Dictionary<string, object?>> ListColumns(string? schema, string tableName)
    {
        var owner = DefaultOwner(schema);
        var sql = """
            SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE, NULLABLE
            FROM ALL_TAB_COLUMNS
            WHERE OWNER = :owner AND TABLE_NAME = :tableName
            ORDER BY COLUMN_ID
            """;

        using var conn = CreateReadonlyConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new OracleParameter("owner", owner));
        cmd.Parameters.Add(new OracleParameter("tableName", tableName.ToUpperInvariant()));

        using var reader = cmd.ExecuteReader();
        var results = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            results.Add(new Dictionary<string, object?>
            {
                ["columnName"] = reader.GetString(0),
                ["dataType"] = reader.GetString(1),
                ["dataLength"] = reader.GetInt32(2),
                ["dataPrecision"] = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                ["dataScale"] = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                ["nullable"] = reader.GetString(5) == "Y"
            });
        }
        return results;
    }

    public List<Dictionary<string, object?>> GetPrimaryKey(string? schema, string tableName)
    {
        var owner = DefaultOwner(schema);
        var sql = """
            SELECT acc.CONSTRAINT_NAME, acc.COLUMN_NAME, acc.POSITION
            FROM ALL_CONSTRAINTS ac
            JOIN ALL_CONS_COLUMNS acc ON acc.OWNER = ac.OWNER AND acc.CONSTRAINT_NAME = ac.CONSTRAINT_NAME
            WHERE ac.OWNER = :owner AND ac.TABLE_NAME = :tableName AND ac.CONSTRAINT_TYPE = 'P'
            ORDER BY acc.POSITION
            """;

        using var conn = CreateReadonlyConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new OracleParameter("owner", owner));
        cmd.Parameters.Add(new OracleParameter("tableName", tableName.ToUpperInvariant()));

        using var reader = cmd.ExecuteReader();
        var results = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            results.Add(new Dictionary<string, object?>
            {
                ["constraintName"] = reader.GetString(0),
                ["columnName"] = reader.GetString(1),
                ["position"] = reader.GetInt32(2)
            });
        }
        return results;
    }

    public List<Dictionary<string, object?>> ListForeignKeys(string? schema, string tableName)
    {
        var owner = DefaultOwner(schema);
        var sql = """
            SELECT
                fk.CONSTRAINT_NAME,
                fkcols.COLUMN_NAME,
                fkcols.POSITION,
                pk.OWNER AS REFERENCED_SCHEMA,
                pk.TABLE_NAME AS REFERENCED_TABLE,
                pkcols.COLUMN_NAME AS REFERENCED_COLUMN
            FROM ALL_CONSTRAINTS fk
            JOIN ALL_CONS_COLUMNS fkcols ON fkcols.OWNER = fk.OWNER AND fkcols.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
            JOIN ALL_CONSTRAINTS pk ON pk.OWNER = fk.R_OWNER AND pk.CONSTRAINT_NAME = fk.R_CONSTRAINT_NAME
            JOIN ALL_CONS_COLUMNS pkcols ON pkcols.OWNER = pk.OWNER AND pkcols.CONSTRAINT_NAME = pk.CONSTRAINT_NAME AND pkcols.POSITION = fkcols.POSITION
            WHERE fk.OWNER = :owner AND fk.TABLE_NAME = :tableName AND fk.CONSTRAINT_TYPE = 'R'
            ORDER BY fk.CONSTRAINT_NAME, fkcols.POSITION
            """;

        using var conn = CreateReadonlyConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new OracleParameter("owner", owner));
        cmd.Parameters.Add(new OracleParameter("tableName", tableName.ToUpperInvariant()));

        using var reader = cmd.ExecuteReader();
        var results = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            results.Add(new Dictionary<string, object?>
            {
                ["constraintName"] = reader.GetString(0),
                ["columnName"] = reader.GetString(1),
                ["position"] = reader.GetInt32(2),
                ["referencedSchema"] = reader.GetString(3),
                ["referencedTable"] = reader.GetString(4),
                ["referencedColumn"] = reader.GetString(5)
            });
        }
        return results;
    }

    public List<Dictionary<string, object?>> DescribeTable(string? schema, string tableName)
    {
        var owner = DefaultOwner(schema);
        var sql = """
            SELECT
                c.COLUMN_NAME, c.DATA_TYPE, c.DATA_LENGTH, c.DATA_PRECISION, c.DATA_SCALE,
                c.NULLABLE, c.DATA_DEFAULT, cc.COMMENTS
            FROM ALL_TAB_COLUMNS c
            LEFT JOIN ALL_COL_COMMENTS cc ON cc.OWNER = c.OWNER AND cc.TABLE_NAME = c.TABLE_NAME AND cc.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.OWNER = :owner AND c.TABLE_NAME = :tableName
            ORDER BY c.COLUMN_ID
            """;

        using var conn = CreateReadonlyConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new OracleParameter("owner", owner));
        cmd.Parameters.Add(new OracleParameter("tableName", tableName.ToUpperInvariant()));

        using var reader = cmd.ExecuteReader();
        var results = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            results.Add(new Dictionary<string, object?>
            {
                ["columnName"] = reader.GetString(0),
                ["dataType"] = reader.GetString(1),
                ["dataLength"] = reader.GetInt32(2),
                ["dataPrecision"] = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                ["dataScale"] = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                ["nullable"] = reader.GetString(5) == "Y",
                ["dataDefault"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                ["comment"] = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
        return results;
    }

    public QueryResult GetTableSample(string? schema, string tableName, int limit)
    {
        var owner = DefaultOwner(schema);
        var safeLimit = Math.Min(limit, _config.FetchMaxRows);
        var sql = $"SELECT * FROM {QuoteIdentifier(owner)}.{QuoteIdentifier(tableName)} WHERE ROWNUM <= {safeLimit}";
        return RunQuery(sql);
    }

    public List<string> ListSchemas()
    {
        using var conn = CreateReadonlyConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT USERNAME FROM ALL_USERS ORDER BY USERNAME";

        using var reader = cmd.ExecuteReader();
        var schemas = new List<string>();
        while (reader.Read())
            schemas.Add(reader.GetString(0));
        return schemas;
    }
}