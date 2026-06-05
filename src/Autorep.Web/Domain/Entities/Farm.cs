namespace Autorep.Web.Domain.Entities;

/// <summary>
/// A dairy operation at a specific location, against which Machine Tests are performed.
/// Farm Details are master data — editable by the owning Tester's Company Administrator
/// and the NZMPTA Super-Administrator — and are independent of the wizard capture flow.
/// A Machine Test references the Farm by id at the time the test is created.
/// </summary>
public class Farm
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Identity ---------------------------------------------------------
    /// <summary>Farm / dairy-operation name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dairy-company supply number, where the farm supplies one.</summary>
    public string? SupplyNumber { get; set; }

    /// <summary>The dairy processor the farm supplies (reference data). Not a Testing Company.</summary>
    public Guid? MilkSupplyCompanyId { get; set; }
    public MilkSupplyCompany? MilkSupplyCompany { get; set; }

    // --- Location ---------------------------------------------------------
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>Town or locality.</summary>
    public string? Town { get; set; }
    /// <summary>Region (reference data — one of the official NZ regions).</summary>
    public Guid? RegionId { get; set; }
    public Region? Region { get; set; }
    public string? PostCode { get; set; }
    /// <summary>NZ rural address (RAPID) number.</summary>
    public string? RapidNumber { get; set; }

    // --- Farmer (owner / primary contact) ---------------------------------
    // Embedded on the Farm for now. If a Farmer can own multiple Farms or a Farm
    // needs multiple contacts, extract a Farmer entity later.
    public string? FarmerName { get; set; }
    public string? ContactPhone { get; set; }
    /// <summary>Optional recipient for upcoming-test reminders (O4).</summary>
    public string? ContactEmail { get; set; }

    public string? Notes { get; set; }

    /// <summary>Soft enable/disable so retired farms drop out of pickers without losing history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Set whenever Farm Details are edited (audit trail also records the change).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<MachineTest> MachineTests { get; set; } = new List<MachineTest>();
}
