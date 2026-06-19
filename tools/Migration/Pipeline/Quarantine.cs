using System.Text;

namespace Nzmpta.AutoRep.Migration.Pipeline;

/// <summary>
/// Row-level quarantine + data-quality log. PII-redacted by design: only the legacy key and a
/// reason code are stored — never names, emails, addresses or phone numbers — so the output CSV is
/// safe to share (consistent with the PII fix-gate).
/// </summary>
public sealed class Quarantine
{
    public readonly record struct Entry(string Table, string Key, string TargetEntity, string Reason, string Severity);

    private readonly List<Entry> _rows = new();

    public void Add(string table, string key, string targetEntity, string reason, string severity = "warn")
        => _rows.Add(new Entry(table, key, targetEntity, reason, severity));

    public int Count => _rows.Count;

    public Dictionary<string, int> ReasonCounts()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in _rows) d[r.Reason] = d.GetValueOrDefault(r.Reason) + 1;
        return d;
    }

    /// <summary>Deterministic CSV (sorted) so re-runs against an unchanged source produce an
    /// identical artifact.</summary>
    public void WriteCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LegacyTable,LegacyKey,TargetEntity,Reason,Severity");
        foreach (var r in _rows
                     .OrderBy(r => r.Table, StringComparer.Ordinal)
                     .ThenBy(r => r.Reason, StringComparer.Ordinal)
                     .ThenBy(r => r.Key, StringComparer.Ordinal))
        {
            sb.Append(Csv(r.Table)).Append(',')
              .Append(Csv(r.Key)).Append(',')
              .Append(Csv(r.TargetEntity)).Append(',')
              .Append(Csv(r.Reason)).Append(',')
              .Append(Csv(r.Severity)).Append('\n');
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }
}
