using Nzmpta.AutoRep.Migration.Pipeline;
using Nzmpta.AutoRep.Migration.Source;

// autorep-migrate — AutoRep O1 legacy data-migration tool.
// Commands (only validate-source is implemented in this increment):
//   validate-source [--conn "<legacy connection string>"]   read-only pre-flight integrity report
//   dry-run         --target staging                          (next increment) idempotent upsert into staging
//   cutover         --target production --confirm "<token>"   (next increment) guarded single-shot production run

const string DefaultLegacyConn =
    "Server=localhost,1433;Database=Autorep_bak;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

var (command, options) = ParseArgs(args);

string legacyConn =
    options.GetValueOrDefault("conn")
    ?? Environment.GetEnvironmentVariable("AUTOREP_LEGACY_CONN")
    ?? DefaultLegacyConn;

switch (command)
{
    case "validate-source":
        return await SourceValidator.RunAsync(legacyConn);

    case "dry-run":
    case "cutover":
    {
        var targetConn =
            options.GetValueOrDefault("target-conn")
            ?? Environment.GetEnvironmentVariable("AUTOREP_TARGET_CONN");
        if (string.IsNullOrWhiteSpace(targetConn))
        {
            Console.Error.WriteLine("Set the target connection string via $AUTOREP_TARGET_CONN or --target-conn.");
            return 2;
        }

        var outDir = options.GetValueOrDefault("out")
            ?? Path.Combine(AppContext.BaseDirectory, "migration-output");
        var isCutover = command == "cutover";

        if (isCutover)
        {
            var refusal = await CutoverGuard.CheckAsync(targetConn, options.GetValueOrDefault("confirm"));
            if (refusal is not null)
            {
                Console.Error.WriteLine($"CUTOVER REFUSED: {refusal}");
                return 3;
            }
        }

        int? limit = int.TryParse(options.GetValueOrDefault("limit"), out var lim) ? lim : null;

        var runner = new MigrationRunner(new MigrationRunner.Options(legacyConn, targetConn, outDir, isCutover, limit));
        return await runner.RunAsync();
    }

    case "":
    case "help":
    case "--help":
    case "-h":
        PrintHelp();
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 1;
}

static (string Command, Dictionary<string, string> Options) ParseArgs(string[] args)
{
    var command = "";
    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.StartsWith("--", StringComparison.Ordinal))
        {
            var key = a[2..];
            var val = "true";
            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                val = key[(eq + 1)..];
                key = key[..eq];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                val = args[++i];
            }

            opts[key] = val;
        }
        else if (command.Length == 0)
        {
            command = a;
        }
    }

    return (command, opts);
}

static void PrintHelp()
{
    Console.WriteLine("autorep-migrate — AutoRep O1 legacy data-migration tool");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  autorep-migrate validate-source [--conn \"<legacy connection string>\"]");
    Console.WriteLine("      Read-only pre-flight: connects to the legacy AutoRep DB and reports row counts,");
    Console.WriteLine("      duplicate-GUID hazards, satellite join-key integrity, and the migration-relevant");
    Console.WriteLine("      data-quality numbers. Modifies nothing.");
    Console.WriteLine();
    Console.WriteLine("  dry-run  --target staging                        (not yet implemented)");
    Console.WriteLine("  cutover  --target production --confirm \"<token>\" (not yet implemented)");
    Console.WriteLine();
    Console.WriteLine("Connection string resolution order: --conn, then $AUTOREP_LEGACY_CONN, then the");
    Console.WriteLine("local default (localhost,1433 / Autorep_bak / Integrated Security).");
}
