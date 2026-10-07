using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Api;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// An administrator's edit of a completed test, saved as the test's next version: O2 for a
/// Super-Administrator (any field the wizard edits) and PRD stories 49–50 for a Company
/// Administrator (the fault summary's recommendations and general comments, on their own company's
/// tests). Nothing is edited in place. The new version stays the original tester's — so it reaches
/// their device on its next pull and their original locks — and names the administrator as the
/// amendment's author, with the reason they gave.
///
/// The amendment record (what changed, in words) is built in the browser by the same TypeScript the
/// tester's amendments use, since the field labels live there. Everything the server can check, it
/// checks against the stored version the edit was made from, never against what the browser says:
/// which fields changed, that the history so far is untouched, and that the edit was made from the
/// test's current version.
/// </summary>
public sealed class AdminVersioning(AutorepDbContext db, Reconciliation reconciliation)
{
    public const int MaxReasonLength = 500;

    /// <summary>Who is editing, as the new version records them.</summary>
    public sealed record Editor(string Id, string UserName, string DisplayName, bool IsSuperAdmin)
    {
        public string RoleLabel => Roles.Label(IsSuperAdmin ? Roles.SuperAdministrator : Roles.CompanyAdministrator);
        public string Scope => IsSuperAdmin ? EditScope.Full : EditScope.Summary;
    }

    /// <summary>What an administrator may change.</summary>
    public static class EditScope
    {
        /// <summary>Every field the wizard edits — a Super-Administrator.</summary>
        public const string Full = "full";

        /// <summary>The fault summary's per-fault recommendations and the general comments under them
        /// — a Company Administrator. Farm details are edited at /Admin/Farms, not here.</summary>
        public const string Summary = "summary";
    }

    /// <summary>Why a version can't be edited.</summary>
    public static class Blocked
    {
        /// <summary>Not signed off yet: still the tester's draft, on their device.</summary>
        public const string InProgress = "in-progress";

        /// <summary>A test migrated from AutoRep Plus: it reprints from its as-recorded verdicts.</summary>
        public const string Migrated = "migrated";

        /// <summary>No capture payload (a header-only row from before payloads were synced).</summary>
        public const string NoRecord = "no-record";

        /// <summary>A later version exists; edits are made from the current one.</summary>
        public const string Superseded = "superseded";

        /// <summary>Soft-deleted: restore it first.</summary>
        public const string Deleted = "deleted";
    }

    /// <summary>Whether an edit in <paramref name="scope"/> may change <paramref name="unit"/>
    /// (a <see cref="PayloadUnits"/> field path). Nobody changes <see cref="PayloadUnits.Fixed"/>.</summary>
    public static bool MayChange(string scope, string unit) =>
        !PayloadUnits.Fixed.Contains(unit)
        && (scope == EditScope.Full
            || unit == "notes"
            || unit.StartsWith("recommendations.", StringComparison.Ordinal));

    /// <summary>Why <paramref name="version"/> can't be edited, or null when it can.
    /// <paramref name="head"/> is its test's current version.</summary>
    public static string? BlockedReason(MachineTest version, MachineTest? head)
    {
        if (version.IsDeleted) return Blocked.Deleted;
        if (version.MarkedCompleteAt is null) return Blocked.InProgress;
        var payload = PayloadUnits.Parse(version.PayloadJson);
        if (version.ClientId is null || payload is null) return Blocked.NoRecord;
        if (PayloadUnits.IsLegacy(payload)) return Blocked.Migrated;
        if (head is null || head.Id != version.Id) return Blocked.Superseded;
        return null;
    }

    /// <summary>A version's edit state for the viewer: why it can't be edited (null if it can), the
    /// test's current version, and the tester's unfinished new version of it if there is one.</summary>
    public sealed record State(string? Blocked, MachineTest? Head, MachineTest? Draft);

    public async Task<State> StateAsync(MachineTest version, CancellationToken ct)
    {
        var versions = await TestLineage.VersionsAsync(db, version, ct);
        var head = TestLineage.Head(versions);
        var draft = TestLineage.SuccessorsOf(versions, version)
            .Where(v => v.MarkedCompleteAt is null)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault();
        return new State(BlockedReason(version, head), head, draft);
    }

    public abstract record Result;
    public sealed record Saved(MachineTest Version) : Result;
    public sealed record Missing : Result;
    /// <summary>The version can't be edited (<see cref="Blocked"/>).</summary>
    public sealed record Refused(string Reason) : Result;
    /// <summary>The edit was made from a version that is no longer current.</summary>
    public sealed record Stale(MachineTest Latest) : Result;
    public sealed record Invalid(string Message) : Result;
    /// <summary>The edit changed fields the editor's role may not change.</summary>
    public sealed record OutOfScope(IReadOnlyList<string> Fields) : Result;

    // Rewriting a payload must not re-encode its text (macrons in Māori place names): see PulsationPayload.
    private static readonly JsonSerializerOptions Write = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Saves <paramref name="payloadJson"/> — the edited test, as the browser built it from the
    /// version <paramref name="baseId"/> — as that test's next version. <paramref name="scope"/> is
    /// the editor's readable set of tests (a Company Administrator's company), so an out-of-scope id
    /// is simply Missing.
    /// </summary>
    public async Task<Result> SaveAsync(
        IQueryable<MachineTest> scope, Guid baseId, string? payloadJson, string? reason, Editor editor,
        CancellationToken ct)
    {
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why)) return new Invalid("Give a reason for the change — it's kept with the new version.");
        if (why.Length > MaxReasonLength) return new Invalid($"Keep the reason under {MaxReasonLength} characters.");

        var basis = await scope.Include(t => t.Configuration).FirstOrDefaultAsync(t => t.Id == baseId, ct);
        if (basis is null) return new Missing();

        var versions = await TestLineage.VersionsAsync(db, basis, ct);
        var head = TestLineage.Head(versions);
        switch (BlockedReason(basis, head))
        {
            case Blocked.Superseded when head is not null: return new Stale(head);
            case { } blocked: return new Refused(blocked);
        }

        var basePayload = PayloadUnits.Parse(basis.PayloadJson)!;
        var edited = PayloadUnits.Parse(payloadJson);
        if (edited is null) return new Invalid("The edited test didn't arrive.");

        var changed = PayloadUnits.Changed(basePayload, edited);
        var refused = changed.Where(unit => !MayChange(editor.Scope, unit)).ToList();
        if (refused.Count > 0) return new OutOfScope(refused);

        // The history so far stays exactly as it was; the edit adds one record — its own — to it.
        var history = basePayload["amendments"] as JsonArray ?? [];
        if (edited["amendments"] is not JsonArray amendments
            || amendments.Count != history.Count + 1
            || history.Where((earlier, i) => !PayloadUnits.Same(earlier, amendments[i])).Any()
            || amendments[^1] is not JsonObject record
            || record["changes"] is not JsonArray)
            return new OutOfScope(["amendments"]);

        var now = DateTimeOffset.UtcNow;
        var at = Iso(now);
        var clientId = Guid.NewGuid();
        var version = basis.Version + 1;
        // The test was completed when the tester signed it off, and an administrator's correction
        // doesn't change that: the report's "Tested" date and the list's completion order stay put.
        // When the edit was made is the amendment record's amendedAt.
        var completedAt = basis.MarkedCompleteAt!.Value;

        // The server's own bookkeeping, whatever the browser sent.
        edited["id"] = clientId.ToString();
        edited["version"] = version;
        edited["supersedesId"] = basis.ClientId!.Value.ToString();
        edited["createdAt"] = at;
        edited["updatedAt"] = at;
        edited["markedCompleteAt"] = Iso(completedAt);
        edited["syncState"] = "uploaded";
        edited["everUploaded"] = true;
        edited["currentStep"] = "ReviewSignOff";
        edited.Remove("readonly");
        edited.Remove("mergedFromId");
        edited.Remove("deletedOnServer");
        // The tester's attestations describe the inspection, which the edit didn't redo: they stand.
        edited["attestations"] = (basePayload["attestations"] as JsonArray)?.DeepClone() ?? new JsonArray();

        record["version"] = version;
        record["amendedAt"] = at;
        record["amendedBy"] = editor.UserName;
        record["amendedByName"] = editor.DisplayName;
        record["amendedByRole"] = editor.RoleLabel;
        record["reason"] = why;
        record["baseVersion"] = basis.Version;
        record["baseCompletedAt"] = basis.MarkedCompleteAt is { } done ? Iso(done) : null;
        record.Remove("baseUnavailable");
        record.Remove("merge");

        // An attachment the browser held only as a pointer gets its bytes back from the base.
        var payload = await PulsationPayload.WithStoredBytesAsync(edited.ToJsonString(Write), [basis.ClientId.Value],
            id => db.MachineTests.Where(t => t.TesterId == basis.TesterId && t.ClientId == id)
                .Select(t => t.PayloadJson).FirstOrDefaultAsync(ct));

        var row = new MachineTest
        {
            ClientId = clientId,
            TesterId = basis.TesterId,
            TestingCompanyId = basis.TestingCompanyId,
            FarmId = basis.FarmId,
            RootClientId = TestLineage.KeyOf(basis),
            AuthorId = editor.Id,
            Version = version,
            SupersedesClientId = basis.ClientId,
            CreatedAt = now,
            UpdatedAt = now,
            MarkedCompleteAt = completedAt,
            Notes = edited["notes"] is JsonValue notes && notes.TryGetValue<string>(out var text) ? text : null,
            NextTestDate = edited["nextTestDate"] is JsonValue next && next.TryGetValue<string>(out var day)
                && DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)
                    ? due
                    : basis.NextTestDate,
            PayloadJson = payload,
        };
        // The configuration columns carry over; a Super-Administrator's edit may then change them.
        if (basis.Configuration is not null) SyncController.ApplyConfig(row, SyncController.ToDto(basis.Configuration));
        if (editor.IsSuperAdmin && edited["config"] is JsonObject config)
        {
            try
            {
                SyncController.ApplyConfig(row, config.Deserialize<SyncController.ConfigDto>(JsonSerializerOptions.Web));
            }
            catch (JsonException)
            {
                return new Invalid("The machine configuration in the edit couldn't be read.");
            }
        }
        db.MachineTests.Add(row);
        basis.SuccessorStamp = Guid.NewGuid();

        // The tester has an unfinished new version of this test on their device: it is combined with
        // this one when they sign it off. Recorded now, so the collision is on file from the start.
        foreach (var draft in TestLineage.SuccessorsOf(versions, basis).Where(v => v.MarkedCompleteAt is null && v.ClientId is not null))
            await reconciliation.NotePendingAsync(basis, row, draft.ClientId!.Value, SyncConflictSource.Save, ct);

        db.AuditEntries.Add(new AuditEntry
        {
            Actor = editor.Id,
            EntityType = nameof(MachineTest),
            EntityKey = row.Id.ToString(),
            Operation = "AdminVersionCreated",
            // Field paths and the reason only — never the values, which can carry farm PII
            // (the audit store deliberately doesn't keep it; see AuditInterceptor).
            AfterJson = JsonSerializer.Serialize(new
            {
                baseId = basis.Id,
                baseClientId = basis.ClientId,
                clientId,
                version,
                role = editor.RoleLabel,
                reason = why,
                changed,
            }, Write),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else's version of this test landed between the check and the save.
            db.ChangeTracker.Clear();
            var fresh = await db.MachineTests.FirstAsync(t => t.Id == basis.Id, ct);
            var latest = TestLineage.Head(await TestLineage.VersionsAsync(db, fresh, ct));
            return latest is not null && latest.Id != fresh.Id ? new Stale(latest) : new Refused(Blocked.Superseded);
        }
        return new Saved(row);
    }

    /// <summary>A timestamp the way the device writes them (Date.toISOString).</summary>
    public static string Iso(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
