using OracleMcpServer.Guards;
using Xunit;

namespace OracleMcpServer.Tests;

public sealed class SqlGuardTests
{
    [Fact]
    public void ValidateReadOnlySql_SimpleSelect_ReturnsNormalized()
    {
        var result = SqlGuard.ValidateReadOnlySql("SELECT * FROM dual");
        Assert.Equal("SELECT * FROM dual", result);
    }

    [Fact]
    public void ValidateReadOnlySql_WithClause_Allowed()
    {
        var sql = "WITH cte AS (SELECT 1 FROM dual) SELECT * FROM cte";
        var result = SqlGuard.ValidateReadOnlySql(sql);
        Assert.Equal(sql, result);
    }

    [Fact]
    public void ValidateReadOnlySql_Empty_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql(""));
    }

    [Fact]
    public void ValidateReadOnlySql_Insert_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("INSERT INTO t VALUES (1)"));
    }

    [Fact]
    public void ValidateReadOnlySql_Update_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("UPDATE t SET x = 1"));
    }

    [Fact]
    public void ValidateReadOnlySql_Delete_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("DELETE FROM t"));
    }

    [Fact]
    public void ValidateReadOnlySql_Drop_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("DROP TABLE t"));
    }

    [Fact]
    public void ValidateReadOnlySql_Create_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("CREATE TABLE t (x INT)"));
    }

    [Fact]
    public void ValidateReadOnlySql_SelectForUpdate_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("SELECT * FROM t FOR UPDATE"));
    }

    [Fact]
    public void ValidateReadOnlySql_MultiStatement_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("SELECT 1 FROM dual; SELECT 2 FROM dual"));
    }

    [Fact]
    public void ValidateReadOnlySql_ForbiddenKeywordInStringLiteral_Allowed()
    {
        var sql = "SELECT * FROM t WHERE name = 'INSERT'";
        var result = SqlGuard.ValidateReadOnlySql(sql);
        Assert.Equal(sql, result);
    }

    [Fact]
    public void ValidateReadOnlySql_ForbiddenKeywordInComment_Allowed()
    {
        var sql = "SELECT 1 /* DROP */ FROM dual";
        var result = SqlGuard.ValidateReadOnlySql(sql);
        Assert.Equal("SELECT 1 FROM dual", result);
    }

    [Fact]
    public void ValidateReadOnlySql_CaseInsensitive_Blocked()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("select * from t; delete from t"));
    }

    [Fact]
    public void NormalizeSql_Whitespace_Collapsed()
    {
        var result = SqlGuard.NormalizeSql("SELECT   *\nFROM  dual");
        Assert.Equal("SELECT * FROM dual", result);
    }

    [Fact]
    public void NormalizeSql_Comments_Removed()
    {
        var result = SqlGuard.NormalizeSql("SELECT 1 -- inline comment\nFROM dual");
        Assert.Equal("SELECT 1 FROM dual", result);
    }

    [Fact]
    public void NormalizeSql_BlockComment_Removed()
    {
        var result = SqlGuard.NormalizeSql("SELECT /* block comment */ 1 FROM dual");
        Assert.Equal("SELECT 1 FROM dual", result);
    }

    [Fact]
    public void ValidateReadOnlySql_Truncate_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("TRUNCATE TABLE t"));
    }

    [Fact]
    public void ValidateReadOnlySql_Merge_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.x = s.x"));
    }

    [Fact]
    public void ValidateReadOnlySql_Alter_Throws()
    {
        Assert.Throws<SqlGuardError>(() => SqlGuard.ValidateReadOnlySql("ALTER TABLE t ADD (x INT)"));
    }
}