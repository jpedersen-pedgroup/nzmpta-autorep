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
    private const int BatchSize = 20;
    /// <summary>A row seen changing this many times in one pass is left for the next pass.</summary>
    private const int MaxRetriesPerRow = 3;

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
        // Rows left inline (a dry run, unreadable ones) still match, and sort first among what's
        // left — the pass works in order — so the next page starts past them.
        var leftInline = 0;
        var retries = new Dictionary<Guid, int>();

        while (!ct.IsCancellationRequested)
        {
            var batch = await _db.MachineTests.AsNoTracking()
                // A cheap filter in SQL; PulsationPayload decides whether it really is an attachment.
                .Where(t => t.PayloadJson != null && t.PayloadJson.Contains("\"base64\""))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Skip(leftInline).Take(BatchSize)
                .Select(t => new Candidate(t.Id, t.TesterId, t.ClientId, t.UpdatedAt, t.PayloadJson!))
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                var (outcome, size) = await MoveAsync(row, dryRun, ct);
                switch (outcome)
                {
                    case Outcome.Moved:
                        inline++; moved++; bytes += size;
                        break;
                    case Outcome.WouldMove:
                        inline++; wouldMove++; bytes += size; leftInline++;
                        break;
                    case Outcome.NotInline:
                        leftInline++;
                        break;
                    case Outcome.Unreadable:
                        inline++; unreadable++; leftInline++;
                        break;
                    case Outcome.ChangedMeanwhile:
                        // Read again next page: it may have dropped out, or still hold bytes.
                        retries[row.Id] = retries.GetValueOrDefault(row.Id) + 1;
                        if (retries[row.Id] >= MaxRetriesPerRow) { changed++; leftInline++; }
                        break;
                    case Outcome.StoreDown:
                        failed++;
                        Log(dryRun, inline, moved, wouldMove, bytes, changed, unreadable, failed);
                        return new PulsationBackfillResult(inline, moved, wouldMove, bytes, changed, unreadable, failed);
                }
            }
        }
        Log(dryRun, inline, moved, wouldMove, bytes, changed, unreadable, failed);
        return new PulsationBackfillResult(inline, moved, wouldMove, bytes, changed, unreadable, failed);
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
        if (!await ReplacePayloadIfUnchangedAsync(row.Id, row.UpdatedAt, pointer, ct))
            return (Outcome.ChangedMeanwhile, 0); // the stored object is harmless: content-addressed

        _db.AuditEntries.Add(new AuditEntry
        {
            Actor = "system",
            EntityType = nameof(MachineTest),
            EntityKey = row.Id.ToString(),
            Operation = "PulsationPdfMovedToStore",
            AfterJson = JsonSerializer.Serialize(new { blobKey = key, sha256 = sha, sizeBytes = bytes.Length }),
        });
        await _db.SaveChangesAsync(ct);
        return (Outcome.Moved, bytes.Length);
    }

    /// <summary>Writes the payload only if the row's UpdatedAt is still what was read — every push
    /// stamps it — so a push landing mid-pass is never overwritten with an older payload. A single
    /// conditional UPDATE on SQL Server; the in-memory provider the tests use has no
    /// ExecuteUpdate, and nothing writes concurrently there.</summary>
    private async Task<bool> ReplacePayloadIfUnchangedAsync(Guid id, DateTimeOffset seenUpdatedAt, string payload, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.MachineTests
                .Where(t => t.Id == id && t.UpdatedAt == seenUpdatedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.PayloadJson, payload), ct) == 1;
        }
        var row = await _db.MachineTests.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null || row.UpdatedAt != seenUpdatedAt) return false;
        row.PayloadJson = payload;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// Runs the backfill once, a minute after startup, in the mode <c>PdfStore:PulsationBackfill</c>
/// says: <c>DryRun</c> unless set (it only reads, and logs what it would move), <c>Run</c> to move,
/// <c>Off</c> to skip — always Off under test. It is a background pass, never in a request's way,
/// and nothing it meets can take the app down: a failure is logged and the next start tries again.
/// </summary>
public sealed class PulsationBackfillService : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly PulsationBackfillMode _mode;
    private readonly ILogger<PulsationBackfillService> _log;

    public PulsationBackfillService(IServiceScopeFactory scopes, IConfiguration config, IHostEnvironment env, ILogger<PulsationBackfillService> log)
    {
        _scopes = scopes;
        _log = log;
        var mode = ModeFor(config, env);
        if (mode is null)
            _log.LogError("PdfStore:PulsationBackfill '{Configured}' is not one of Off, DryRun, Run — the backfill won't run", config["PdfStore:PulsationBackfill"]);
        _mode = mode ?? PulsationBackfillMode.Off;
    }

    /// <summary>The configured mode; null when the setting isn't one (a typo mustn't stop the app).</summary>
    public static PulsationBackfillMode? ModeFor(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["PdfStore:PulsationBackfill"];
        if (string.IsNullOrWhiteSpace(configured))
            return env.IsEnvironment("Testing") ? PulsationBackfillMode.Off : PulsationBackfillMode.DryRun;
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
