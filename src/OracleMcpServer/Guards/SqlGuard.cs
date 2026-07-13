using System.Text.RegularExpressions;

namespace OracleMcpServer.Guards;

public sealed class SqlGuardError : Exception
{
    public SqlGuardError(string message) : base(message) { }
}

public static partial class SqlGuard
{
    private static readonly Regex WhitespaceRegex = MyRegex();
    private static readonly Regex ForbiddenTokenRegex = ForbiddenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MyRegex();

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|RENAME|GRANT|REVOKE|BEGIN|DECLARE|CALL|EXECUTE|COMMIT|ROLLBACK|FOR)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ForbiddenRegex();

    private static (string cleaned, string masked) StripCommentsAndMaskLiterals(string sql)
    {
        var cleaned = new char[sql.Length];
        var masked = new char[sql.Length];
        var i = 0;
        var length = sql.Length;
        var state = "normal";

        while (i < length)
        {
            var ch = sql[i];
            var next = i + 1 < length ? sql[i + 1] : '\0';

            switch (state)
            {
                case "normal":
                    if (ch == '\'')
                    {
                        cleaned[i] = ch;
                        masked[i] = ' ';
                        state = "single_quote";
                    }
                    else if (ch == '"')
                    {
                        cleaned[i] = ch;
                        masked[i] = ' ';
                        state = "double_quote";
                    }
                    else if (ch == '-' && next == '-')
                    {
                        cleaned[i] = ' ';
                        cleaned[i + 1] = ' ';
                        masked[i] = ' ';
                        masked[i + 1] = ' ';
                        i++;
                        state = "line_comment";
                    }
                    else if (ch == '/' && next == '*')
                    {
                        cleaned[i] = ' ';
                        cleaned[i + 1] = ' ';
                        masked[i] = ' ';
                        masked[i + 1] = ' ';
                        i++;
                        state = "block_comment";
                    }
                    else
                    {
                        cleaned[i] = ch;
                        masked[i] = ch;
                    }
                    break;

                case "single_quote":
                    cleaned[i] = ch;
                    masked[i] = ' ';
                    if (ch == '\'')
                    {
                        if (next == '\'')
                        {
                            i++;
                            cleaned[i] = next;
                            masked[i] = ' ';
                        }
                        else
                        {
                            state = "normal";
                        }
                    }
                    break;

                case "double_quote":
                    cleaned[i] = ch;
                    masked[i] = ' ';
                    if (ch == '"')
                    {
                        state = "normal";
                    }
                    break;

                case "line_comment":
                    if (ch == '\n')
                    {
                        cleaned[i] = ch;
                        masked[i] = ch;
                        state = "normal";
                    }
                    else
                    {
                        cleaned[i] = ' ';
                        masked[i] = ' ';
                    }
                    break;

                case "block_comment":
                    if (ch == '*' && next == '/')
                    {
                        cleaned[i] = ' ';
                        cleaned[i + 1] = ' ';
                        masked[i] = ' ';
                        masked[i + 1] = ' ';
                        i++;
                        state = "normal";
                    }
                    else
                    {
                        cleaned[i] = ' ';
                        masked[i] = ' ';
                    }
                    break;
            }
            i++;
        }

        return (new string(cleaned), new string(masked));
    }

    public static string NormalizeSql(string sql)
    {
        var (cleaned, _) = StripCommentsAndMaskLiterals(sql);
        return WhitespaceRegex.Replace(cleaned, " ").Trim();
    }

    public static string ValidateReadOnlySql(string sql)
    {
        var (cleaned, masked) = StripCommentsAndMaskLiterals(sql);
        var normalized = WhitespaceRegex.Replace(cleaned, " ").Trim();
        var maskedNormalized = WhitespaceRegex.Replace(masked, " ").Trim();

        if (string.IsNullOrEmpty(normalized))
            throw new SqlGuardError("SQL cannot be empty");

        if (masked.Contains(';'))
            throw new SqlGuardError("Only a single SQL statement is allowed");

        if (!Regex.IsMatch(maskedNormalized, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            throw new SqlGuardError("Only SELECT statements are allowed");

        if (ForbiddenTokenRegex.IsMatch(maskedNormalized))
            throw new SqlGuardError("SQL contains a forbidden keyword for read-only mode");

        return normalized;
    }
}