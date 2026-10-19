using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Data;

/// <summary>
/// The single definition of which farms are "in a Testing Company's scope", shared by the
/// tester farm picker, the farm snapshot API, the sync farm-link and the Admin farm pages so
/// the surfaces can't drift apart: farms the company set up (CreatedByTestingCompanyId) or
/// farms with a Machine Test done for the company. A test's company is the one stamped when it
/// was first sent (MachineTest.TestingCompanyId), not its tester's current company, so a tester
/// who moves to another company leaves the farms they tested behind with the old one (the same
/// rule TestScope applies to the tests themselves). Callers not attached to a company fall back
/// to farms they personally tested.
/// </summary>
public static class FarmScope
{
    // companyId is unwrapped to a non-nullable local, as in TestScope: comparing two nullable Guids
    // would make "no company" match a test with no company under LINQ-to-Objects but not in SQL.
    public static IQueryable<Farm> InCompanyScope(
        this IQueryable<Farm> farms, AutorepDbContext db, Guid? companyId, string? userId)
        => companyId is { } company
            ? farms.Where(f => f.CreatedByTestingCompanyId == company
                || db.MachineTests.Any(t => t.FarmId == f.Id && t.TestingCompanyId == company))
            : farms.Where(f => db.MachineTests.Any(t => t.FarmId == f.Id && t.TesterId == userId));

    /// <summary>
    /// Where a tester's sync push may link its test: their company's scope, plus any farm they have
    /// personally tested before. After a move to another company their device can still hold farm ids
    /// from the old company's book; testing one of those again must link to that farm rather than mint
    /// a duplicate. The new company then gains the farm through the new test, which is stamped for it.
    /// </summary>
    public static IQueryable<Farm> LinkableBySync(
        this IQueryable<Farm> farms, AutorepDbContext db, Guid? companyId, string testerId)
        => companyId is { } company
            ? farms.Where(f => f.CreatedByTestingCompanyId == company
                || db.MachineTests.Any(t => t.FarmId == f.Id
                    && (t.TestingCompanyId == company || t.TesterId == testerId)))
            : farms.InCompanyScope(db, null, testerId);
}
