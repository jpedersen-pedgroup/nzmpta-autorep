using Microsoft.Data.SqlClient;

namespace Nzmpta.AutoRep.Migration.Pipeline;

/// <summary>
/// Guards the single-shot production cutover. Refuses unless the target is empty of migrated tests,
/// the operator passes a matching GO-LIVE confirmation token, and the target is not an Australian
/// region (NZ data-residency). Dry-runs against staging do NOT use this.
/// </summary>
public static class CutoverGuard
{
    public static async Task<string?> CheckAsync(string targetConn, string? confirmToken)
    {
        if (string.IsNullOrWhiteSpace(confirmToken) || !confirmToken.StartsWith("GO-LIVE", StringComparison.Ordinal))
            return "missing/invalid --confirm token (must start with \"GO-LIVE\").";

        var builder = new SqlConnectionStringBuilder(targetConn);
        if (builder.DataSource.Contains("australiaeast", StringComparison.OrdinalIgnoreCase))
            return "target server appears to be in australiaeast — NZ data-residency requires newzealandnorth.";

        try
        {
            await using var c = new SqlConnection(targetConn);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.MachineTests;", c);
            var n = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            if (n > 0)
                return $"target already contains {n:n0} MachineTests — production cutover requires an empty target.";
        }
        catch (Exception ex)
        {
            return $"could not verify target is empty: {ex.Message}";
        }

        return null; // cleared
    }
}
