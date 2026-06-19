using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Nzmpta.AutoRep.Migration.Source;

/// <summary>Minimal read-only ADO helpers for the legacy source DB. DBNull is normalised to null
/// at read time, so callers work with plain <c>object?</c> values.</summary>
public static class Db
{
    public static List<Dictionary<string, object?>> Query(SqlConnection c, string sql)
    {
        var list = new List<Dictionary<string, object?>>();
        using var cmd = new SqlCommand(sql, c) { CommandTimeout = 600 };
        using var r = cmd.ExecuteReader();
        var cols = new string[r.FieldCount];
        for (var i = 0; i < r.FieldCount; i++) cols[i] = r.GetName(i);
        while (r.Read())
        {
            var row = new Dictionary<string, object?>(cols.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cols.Length; i++)
            {
                var v = r.GetValue(i);
                row[cols[i]] = v is DBNull ? null : v;
            }
            list.Add(row);
        }
        return list;
    }

    /// <summary>SQL uniqueidentifier literal list for an IN (...) clause. GUIDs are safe to inline.</summary>
    public static string GuidInList(IEnumerable<Guid> guids) =>
        string.Join(",", guids.Select(g => $"'{g}'"));
}

/// <summary>Safe, type-tolerant accessors over a legacy row dictionary.</summary>
public static class Row
{
    public static string? Str(IReadOnlyDictionary<string, object?> r, string col)
    {
        if (!r.TryGetValue(col, out var v) || v is null) return null;
        var s = v.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static string? Str(IReadOnlyDictionary<string, object?> r, string col, int maxLen)
    {
        var s = Str(r, col);
        return s is null ? null : s.Length <= maxLen ? s : s[..maxLen];
    }

    public static int? Int(IReadOnlyDictionary<string, object?> r, string col)
    {
        if (!r.TryGetValue(col, out var v) || v is null) return null;
        switch (v)
        {
            case int i: return i;
            case long l: return (int)l;
            case short sh: return sh;
            case byte b: return b;
            case decimal d: return (int)d;
            case double db: return (int)db;
        }
        var s = v.ToString();
        if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return n;
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dd)) return (int)dd;
        return null;
    }

    public static bool Truthy(IReadOnlyDictionary<string, object?> r, string col)
    {
        if (!r.TryGetValue(col, out var v) || v is null) return false;
        switch (v)
        {
            case bool b: return b;
            case int i: return i != 0;
            case long l: return l != 0;
            case short sh: return sh != 0;
            case byte by: return by != 0;
            case decimal d: return d != 0;
            case double db: return db != 0;
        }
        var s = v.ToString()?.Trim().ToLowerInvariant();
        return s is "1" or "true" or "yes" or "y" or "t";
    }

    public static DateTime? Date(IReadOnlyDictionary<string, object?> r, string col)
    {
        if (!r.TryGetValue(col, out var v) || v is null) return null;
        if (v is DateTime dt) return dt;
        return DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) ? p : null;
    }
}
