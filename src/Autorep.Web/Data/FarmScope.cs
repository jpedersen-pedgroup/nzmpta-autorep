using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Data;

/// <summary>
/// The single definition of which farms are "in a Testing Company's scope", shared by the
/// tester farm picker, the farm snapshot API, the sync farm-link and the Admin farm pages so
/// the surfaces can't drift apart: farms the company set up (CreatedByTestingCompanyId) or
/// farms any of its testers has a Machine Test against. Callers not attached to a company
/// fall back to farms they personally tested.
/// </summary>
public static class FarmScope
{
    public static IQueryable<Farm> InCompanyScope(
        this IQueryable<Farm> farms, AutorepDbContext db, Guid? companyId, string? userId)
        => companyId is not null
            ? farms.Where(f => f.CreatedByTestingCompanyId == companyId
                || db.MachineTests.Any(t => t.FarmId == f.Id
                    && db.Users.Any(u => u.Id == t.TesterId && u.TestingCompanyId == companyId)))
            : farms.Where(f => db.MachineTests.Any(t => t.FarmId == f.Id && t.TesterId == userId));
}
