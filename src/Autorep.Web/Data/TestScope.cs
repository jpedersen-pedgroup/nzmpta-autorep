using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Data;

/// <summary>
/// The single definition of which Machine Tests a caller may READ, shared by the single-test fetch,
/// the Company-tests list and the Admin all-tests page so the surfaces can't drift apart.
/// A test's company is the stamp written at first upload (MachineTest.TestingCompanyId), not a
/// join through the owner's current company — history stays with the company the work was done for.
/// </summary>
public static class TestScope
{
    /// <summary>Tests performed for this Testing Company.</summary>
    public static IQueryable<MachineTest> InCompany(this IQueryable<MachineTest> tests, Guid companyId)
        => tests.Where(t => t.TestingCompanyId == companyId);

    /// <summary>
    /// What a Tester may read: their OWN tests in any state, plus COMPLETED tests done for the same
    /// Testing Company. A colleague's in-progress work isn't a record yet, so it stays private.
    /// companyId is unwrapped to a non-nullable local deliberately: comparing two nullable Guids
    /// would make "no company" match every other unattached tester under LINQ-to-Objects (true) but
    /// not under SQL Server (three-valued logic) — a security boundary must not depend on that.
    /// </summary>
    public static IQueryable<MachineTest> ReadableByTester(
        this IQueryable<MachineTest> tests, string? testerId, Guid? companyId)
        => companyId is { } company
            ? tests.Where(t => t.TesterId == testerId
                || (t.MarkedCompleteAt != null && t.TestingCompanyId == company))
            : tests.Where(t => t.TesterId == testerId);

    /// <summary>
    /// Drops versions that a later version supersedes, so a re-edited test appears once. The chain
    /// is matched within one tester's rows (s.TesterId == t.TesterId) because ClientId space is
    /// per-tester by design — a push claiming to supersede another tester's ClientId can never
    /// withdraw their test.
    /// </summary>
    public static IQueryable<MachineTest> CurrentVersionsOnly(
        this IQueryable<MachineTest> tests, AutorepDbContext db)
        => tests.Where(t => t.ClientId == null
            || !db.MachineTests.Any(s => s.TesterId == t.TesterId
                && s.SupersedesClientId == t.ClientId));
}
