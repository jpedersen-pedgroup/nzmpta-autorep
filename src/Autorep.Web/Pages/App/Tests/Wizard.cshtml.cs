using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Domain.Wizard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.App.Tests;

// First-cut server-rendered wizard: the step rail is produced by WizardStepResolver, and the
// Machine Configuration step captures the config that drives it. Remaining steps are placeholders
// until the offline (IndexedDB + Preact) wizard is built. Scoped to the owning Tester.
public class WizardModel : PageModel
{
    private readonly AutorepDbContext _db;
    public WizardModel(AutorepDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Step { get; set; }
    [BindProperty] public ConfigInput Config { get; set; } = new();

    public MachineTest Test { get; private set; } = default!;
    public WizardPlan Plan { get; private set; } = default!;
    public WizardStep Current { get; private set; }
    public IReadOnlySet<WizardStep> Completed { get; private set; } = new HashSet<WizardStep>();

    public async Task<IActionResult> OnGetAsync()
    {
        var test = await LoadAsync();
        if (test is null) return RedirectToPage("/App/Tests/Index");

        Test = test;
        var cfg = test.Configuration ?? new MachineConfiguration { MachineTestId = test.Id };
        Plan = WizardStepResolver.Resolve(cfg);
        Current = ParseStep(Step) ?? WizardStep.MachineConfiguration;
        Config = ConfigInput.From(cfg);
        Completed = ComputeCompleted(test);
        return Page();
    }

    public async Task<IActionResult> OnPostConfigAsync()
    {
        var test = await LoadAsync();
        if (test is null) return RedirectToPage("/App/Tests/Index");

        var cfg = test.Configuration ?? new MachineConfiguration { MachineTestId = test.Id };
        Config.ApplyTo(cfg);
        cfg.UpdatedAt = DateTimeOffset.UtcNow;
        if (test.Configuration is null) _db.MachineConfigurations.Add(cfg);
        await _db.SaveChangesAsync();

        // Re-resolve (config may have changed which steps apply) and advance.
        var next = NextAfter(WizardStepResolver.Resolve(cfg), WizardStep.MachineConfiguration);
        return RedirectToPage(new { id = Id, step = next?.ToString() });
    }

    private async Task<MachineTest?> LoadAsync()
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await _db.MachineTests
            .Include(t => t.Farm)
            .Include(t => t.Configuration)
            .FirstOrDefaultAsync(t => t.Id == Id && t.TesterId == testerId);
    }

    public WizardStep? Prev() => Adjacent(-1);
    public WizardStep? Next() => Adjacent(+1);

    private WizardStep? Adjacent(int delta)
    {
        var steps = Plan.Steps.Select(s => s.Step).ToList();
        var i = steps.IndexOf(Current);
        if (i < 0) return null;
        var j = i + delta;
        return j >= 0 && j < steps.Count ? steps[j] : null;
    }

    private static WizardStep? NextAfter(WizardPlan plan, WizardStep step)
    {
        var steps = plan.Steps.Select(s => s.Step).ToList();
        var i = steps.IndexOf(step);
        return i >= 0 && i + 1 < steps.Count ? steps[i + 1] : null;
    }

    private static WizardStep? ParseStep(string? s)
        => Enum.TryParse<WizardStep>(s, out var v) ? v : null;

    // Which steps count as done, for the progress stepper. Grows as steps are implemented;
    // for now: Setup (a farm is attached) and Machine Configuration (saved at least once).
    private static HashSet<WizardStep> ComputeCompleted(MachineTest test)
    {
        var done = new HashSet<WizardStep>();
        if (test.FarmId != Guid.Empty) done.Add(WizardStep.Setup);
        if (test.Configuration is not null) done.Add(WizardStep.MachineConfiguration);
        return done;
    }

    // Editable view of MachineConfiguration (keeps Id/MachineTestId off the form surface).
    public class ConfigInput
    {
        public PlantType PlantType { get; set; } = PlantType.HerringboneLowline;
        public int ClusterCount { get; set; }
        public int? HerdSize { get; set; }
        public string? MilklineSize { get; set; }
        public string? PulsatorModel { get; set; }
        public int PulsatorCount { get; set; }
        public string? ClawModel { get; set; }
        public string? ShellModel { get; set; }
        public string? LinerModel { get; set; }
        public bool LinerVented { get; set; }
        public bool FlushingPulsationSystem { get; set; }
        public int NumberOfVacuumPumps { get; set; } = 1;
        public PumpLubrication PumpLubrication { get; set; } = PumpLubrication.OilLubricated;
        public bool VsdFitted { get; set; }
        public bool IsoPortsAvailable { get; set; } = true;
        public bool HasPulsatorStopSystem { get; set; }
        public bool HasAcr { get; set; }
        public bool HasBailGates { get; set; }
        public bool HasMilkMeters { get; set; }
        public bool HasTeatSprayer { get; set; }
        public bool HasBackingGate { get; set; }
        public bool HasReleaserPump { get; set; }

        public static ConfigInput From(MachineConfiguration c) => new()
        {
            PlantType = c.PlantType,
            ClusterCount = c.ClusterCount,
            HerdSize = c.HerdSize,
            MilklineSize = c.MilklineSize,
            PulsatorModel = c.PulsatorModel,
            PulsatorCount = c.PulsatorCount,
            ClawModel = c.ClawModel,
            ShellModel = c.ShellModel,
            LinerModel = c.LinerModel,
            LinerVented = c.LinerVented,
            FlushingPulsationSystem = c.FlushingPulsationSystem,
            NumberOfVacuumPumps = c.NumberOfVacuumPumps,
            PumpLubrication = c.PumpLubrication,
            VsdFitted = c.VsdFitted,
            IsoPortsAvailable = c.IsoPortsAvailable,
            HasPulsatorStopSystem = c.HasPulsatorStopSystem,
            HasAcr = c.HasAcr,
            HasBailGates = c.HasBailGates,
            HasMilkMeters = c.HasMilkMeters,
            HasTeatSprayer = c.HasTeatSprayer,
            HasBackingGate = c.HasBackingGate,
            HasReleaserPump = c.HasReleaserPump,
        };

        public void ApplyTo(MachineConfiguration c)
        {
            c.PlantType = PlantType;
            c.ClusterCount = ClusterCount;
            c.HerdSize = HerdSize;
            c.MilklineSize = Clean(MilklineSize);
            c.PulsatorModel = Clean(PulsatorModel);
            c.PulsatorCount = PulsatorCount;
            c.ClawModel = Clean(ClawModel);
            c.ShellModel = Clean(ShellModel);
            c.LinerModel = Clean(LinerModel);
            c.LinerVented = LinerVented;
            c.FlushingPulsationSystem = FlushingPulsationSystem;
            c.NumberOfVacuumPumps = NumberOfVacuumPumps;
            c.PumpLubrication = PumpLubrication;
            c.VsdFitted = VsdFitted;
            c.IsoPortsAvailable = IsoPortsAvailable;
            c.HasPulsatorStopSystem = HasPulsatorStopSystem;
            c.HasAcr = HasAcr;
            c.HasBailGates = HasBailGates;
            c.HasMilkMeters = HasMilkMeters;
            c.HasTeatSprayer = HasTeatSprayer;
            c.HasBackingGate = HasBackingGate;
            c.HasReleaserPump = HasReleaserPump;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
