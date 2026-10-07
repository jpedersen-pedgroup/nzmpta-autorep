using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

public enum PulsationBackfillMode
{
    /// <summary>Don't look.</summary>
    Off,

    /// <summary>Count what would move and say so in the log; change nothing.</summary>
    DryRun,

    /// <summary>Move it.</summary>
    Run,
}

/// <summary>What one pass found and did.</summary>
/// <param name="Inline">Tests whose payload still held an analyser PDF inline.</param>
/// <param name="Moved">Moved to the store, payload now a pointer.</param>
/// <param name="WouldMove">A dry run's count of what it would have moved.</param>
/// <param name="Bytes">Bytes of PDF among them (decoded, not base64).</param>
/// <param name="ChangedMeanwhile">Pushed again while the pass ran; the push stored its own PDF (or the next pass will).</param>
/// <param name="Unreadable">Not base64 — the device's, left exactly as it is.</param>
/// <param name="Failed">The store couldn't take them; the pass stopped and the next one tries again.</param>
public sealed record PulsationBackfillResult(
    int Inline, int Moved, int WouldMove, long Bytes, int ChangedMeanwhile, int Unreadable, int Failed);

/// <summary>
/// Moves analyser PDFs written before the PDF store — or while it was down — out of
/// <c>MachineTest.PayloadJson</c> and into the store, leaving the pointer a push now leaves
/// (<see cref="PulsationAttachments"/>). Idempotent: a moved test no longer matches, and a PDF is
/// stored under its own hash, so a second pass (or a second instance) finds nothing left to do.
///
/// A row is only rewritten if it hasn't been pushed since it was read — otherwise the push's newer
/// payload would be overwritten with an older one — and the backfill doesn't touch UpdatedAt: the
/// test's content is unchanged, so no device needs to pull it again. Each move is audited.
/// </summary>
public sealed class PulsationBackfill
{
    /// <summary>A row seen changing this many times in one pass is left for the next pass.</summary>
    private const int MaxRetriesPerRow = 3;

    /// <summary>How long the scan for candidates may take. Finding them means reading every test's
    /// payload: on staging's serverless database, holding the migrated history, it ran past SQL's
    /// default 30 s (8 Oct 2026). It runs once per pass; each candidate is then read by its key.</summary>
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(10);

    private readonly AutorepDbContext _db;
    private readonly IPdfStore _store;
    private readonly ILogger<PulsationBackfill> _log;

    public PulsationBackfill(AutorepDbContext db, IPdfStore store, ILogger<PulsationBackfill> log)
    {
        _db = db;
        _store = store;
        _log = log;
    }

    public sealed record Candidate(Guid Id, string TesterId, Guid? ClientId, DateTimeOffset UpdatedAt, string PayloadJson);

    public enum Outcome { Moved, WouldMove, NotInline, Unreadable, ChangedMeanwhile, StoreDown }

    public async Task<PulsationBackfillResult> RunAsync(bool dryRun, CancellationToken ct)
    {
        int inline = 0, moved = 0, wouldMove = 0, changed = 0, unreadable = 0, failed = 0;
        long bytes = 0;

        // One scan finds the candidates, and only their ids come back; each is then read by its key.
        // A cheap filter in SQL — PulsationPayload decides whether a row really holds an attachment.
        var ids = await WithScanTimeoutAsync(() => _db.MachineTests.AsNoTracking()
            .Where(t => t.PayloadJson != null && t.PayloadJson.Contains("\"base64\""))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(ct));

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;
            for (var attempt = 1; ; attempt++)
            {
                var row = await _db.MachineTests.AsNoTracking()
                    .Where(t => t.Id == id)
                    .Select(t => new Candidate(t.Id, t.TesterId, t.ClientId, t.UpdatedAt, t.PayloadJson!))
                    .FirstOrDefaultAsync(ct);
                if (row is null) break;

                var (outcome, size) = await MoveAsync(row, dryRun, ct);
                // Pushed again between the read and the write: read it again — it may no longer hold
                // bytes, or still does under a newer stamp.
                if (outcome == Outcome.ChangedMeanwhile && attempt < MaxRetriesPerRow) continue;
                switch (outcome)
                {
                    case Outcome.Moved:
                        inline++; moved++; bytes += size;
                        break;
                    case Outcome.WouldMove:
                        inline++; wouldMove++; bytes += size;
                        break;
                    case Outcome.Unreadable:
                        inline++; unreadable++;
                        break;
                    case Outcome.ChangedMeanwhile:
                        changed++;
                        break;
                    case Outcome.StoreDown:
                        failed++;
                        Log(dryRun, inline, moved, wouldMove, bytes, changed, unreadable, failed);
                        return new PulsationBackfillResult(inline, moved, wouldMove, bytes, changed, unreadable, failed);
                }
                break;
            }
        }
        Log(dryRun, inline, moved, wouldMove, bytes, changed, unreadable, failed);
        return new PulsationBackfillResult(inline, moved, wouldMove, bytes, changed, unreadable, failed);
    }

    /// <summary>The scan, allowed <see cref="ScanTimeout"/> rather than the default command timeout
    /// (relational only — the in-memory provider has none).</summary>
    private async Task<T> WithScanTimeoutAsync<T>(Func<Task<T>> scan)
    {
        if (!_db.Database.IsRelational()) return await scan();
        var previous = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(ScanTimeout);
        try
        {
            return await scan();
        }
        finally
        {
            _db.Database.SetCommandTimeout(previous);
        }
    }

    private void Log(bool dryRun, int inline, int moved, int wouldMove, long bytes, int changed, int unreadable, int failed)
    {
        if (failed > 0)
        {
            _log.LogWarning("Pulsation PDF backfill stopped: the PDF store is unavailable. Moved {Moved} before it did", moved);
            return;
        }
        if (dryRun)
            _log.LogInformation(
                "Pulsation PDF backfill (dry run): {Inline} tests hold an analyser PDF inline, {Bytes} bytes; {WouldMove} would move; {Unreadable} aren't base64 and would stay as they are. Set PdfStore:PulsationBackfill=Run to move them",
                inline, bytes, wouldMove, unreadable);
        else
            _log.LogInformation(
                "Pulsation PDF backfill: moved {Moved} analyser PDFs ({Bytes} bytes) out of PayloadJson; {Changed} were pushed again meanwhile; {Unreadable} aren't base64 and stay as they are",
                moved, bytes, changed, unreadable);
    }

    /// <summary>One row: its inline PDF into the store and its payload rewritten to the pointer,
    /// unless it was pushed again since <see cref="Candidate.UpdatedAt"/>.</summary>
    public async Task<(Outcome Outcome, long Bytes)> MoveAsync(Candidate row, bool dryRun, CancellationToken ct)
    {
        if (PulsationPayload.Base64(row.PayloadJson) is not { } base64) return (Outcome.NotInline, 0);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return (Outcome.Unreadable, 0);
        }
        if (dryRun) return (Outcome.WouldMove, bytes.Length);

        var clientId = row.ClientId ?? row.Id;
        var sha = PdfHash.Sha256Hex(bytes);
        var key = PdfKeys.Pulsation(row.TesterId, clientId, sha);
        try
        {
            await _store.PutAsync(PdfContainer.PulsationData, key, bytes, "application/pdf", ct);
        }
        catch (PdfStoreException e)
        {
            _log.LogWarning(e, "Pulsation PDF backfill could not store test {TestId}'s PDF", row.Id);
            return (Outcome.StoreDown, 0);
        }

        var pointer = PulsationPayload.AsStored(row.PayloadJson, sha, holderClientId: null)!;
        AuditEntry Audit() => new()
        {
            Actor = "system",
            EntityType = nameof(MachineTest),
            EntityKey = row.Id.ToString(),
            Operation = "PulsationPdfMovedToStore",
            AfterJson = JsonSerializer.Serialize(new { blobKey = key, sha256 = sha, sizeBytes = bytes.Length }),
        };
        return await ReplacePayloadIfUnchangedAsync(row.Id, row.UpdatedAt, pointer, Audit, ct)
            ? (Outcome.Moved, bytes.Length)
            : (Outcome.ChangedMeanwhile, 0); // the stored object is harmless: content-addressed
    }

    /// <summary>
    /// Writes the payload, and its audit entry, only if the row's UpdatedAt is still what was read —
    /// every push stamps it — so a push landing mid-pass is never overwritten with an older payload.
    /// Both in one transaction: a payload rewritten without its audit entry would never be found
    /// again to put that right (it no longer holds inline bytes). On SQL Server that's a conditional
    /// UPDATE and the audit insert inside the retrying execution strategy (each attempt starts from a
    /// clean change tracker and a fresh audit entry); the in-memory provider the tests use has
    /// neither ExecuteUpdate nor transactions, so there it's one SaveChanges.
    /// </summary>
    private async Task<bool> ReplacePayloadIfUnchangedAsync(
        Guid id, DateTimeOffset seenUpdatedAt, string payload, Func<AuditEntry> audit, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var updated = await _db.MachineTests
                    .Where(t => t.Id == id && t.UpdatedAt == seenUpdatedAt)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.PayloadJson, payload), ct);
                if (updated != 1) return false; // disposing the transaction rolls it back
                _db.AuditEntries.Add(audit());
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            });
        }
        var row = await _db.MachineTests.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null || row.UpdatedAt != seenUpdatedAt) return false;
        row.PayloadJson = payload;
        _db.AuditEntries.Add(audit());
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// Runs the backfill once, a minute after startup, when <c>PdfStore:PulsationBackfill</c> asks:
/// <c>DryRun</c> only reads, and logs what it would move; <c>Run</c> moves it. Off unless set —
/// finding candidates means reading every test's payload, minutes of work on a database holding the
/// migrated history, so it isn't repeated at every start: set it, let one start do the pass, unset
/// it. A background pass, never in a request's way, and nothing it meets can take the app down: a
/// failure is logged and the next start (while still set) tries again.
/// </summary>
public sealed class PulsationBackfillService : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly PulsationBackfillMode _mode;
    private readonly ILogger<PulsationBackfillService> _log;

    public PulsationBackfillService(IServiceScopeFactory scopes, IConfiguration config, ILogger<PulsationBackfillService> log)
    {
        _scopes = scopes;
        _log = log;
        var mode = ModeFor(config);
        if (mode is null)
            _log.LogError("PdfStore:PulsationBackfill '{Configured}' is not one of Off, DryRun, Run — the backfill won't run", config["PdfStore:PulsationBackfill"]);
        _mode = mode ?? PulsationBackfillMode.Off;
    }

    /// <summary>The configured mode — Off when unset; null when the setting isn't a mode (a typo
    /// mustn't stop the app).</summary>
    public static PulsationBackfillMode? ModeFor(IConfiguration config)
    {
        var configured = config["PdfStore:PulsationBackfill"];
        if (string.IsNullOrWhiteSpace(configured)) return PulsationBackfillMode.Off;
        return Enum.TryParse<PulsationBackfillMode>(configured.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_mode == PulsationBackfillMode.Off) return;
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PulsationBackfill>()
                .RunAsync(dryRun: _mode == PulsationBackfillMode.DryRun, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception e)
        {
            _log.LogError(e, "Pulsation PDF backfill failed; it runs again at the next start");
        }
    }
}
