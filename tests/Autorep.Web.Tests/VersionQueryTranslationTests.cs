using System.Data.Common;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.Admin.Tests;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Autorep.Web.Tests;

// The version chain's queries run on SQL Server in production and on the in-memory provider in every
// other test, which translates anything. Each one here runs against SQL Server options with an
// interceptor that throws the moment EF goes to open a connection: translation happens before that,
// so reaching the sentinel proves the query compiles to SQL. Nothing is connected to.
public class VersionQueryTranslationTests
{
    private sealed class Translated : Exception;

    private sealed class NeverConnect : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) => throw new Translated();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) => throw new Translated();
    }

    private static AutorepDbContext SqlServer() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True")
            .AddInterceptors(new NeverConnect())
            .Options);

    private static MachineTest AVersion() => new()
    {
        TesterId = "tester",
        ClientId = Guid.NewGuid(),
        RootClientId = Guid.NewGuid(),
        SupersedesClientId = Guid.NewGuid(),
        MergedFromClientId = Guid.NewGuid(),
    };

    [Fact]
    public async Task Walking_a_tests_versions_translates()
    {
        using var db = SqlServer();
        await db.Invoking(d => TestLineage.VersionsAsync(d, AVersion(), default)).Should().ThrowAsync<Translated>();
    }

    [Fact]
    public async Task Finding_a_new_versions_root_and_adopting_its_descendants_translate()
    {
        using var db = SqlServer();
        await db.Invoking(d => TestLineage.RootForNewVersionAsync(d, "tester", Guid.NewGuid(), Guid.NewGuid(), default))
            .Should().ThrowAsync<Translated>();
        await db.Invoking(d => TestLineage.AdoptDescendantsAsync(d, "tester", Guid.NewGuid(), Guid.NewGuid(), default))
            .Should().ThrowAsync<Translated>();
    }

    [Fact]
    public async Task Collision_checks_translate()
    {
        using var db = SqlServer();
        var reconciliation = new Reconciliation(db);
        await reconciliation.Invoking(r => r.CollisionAsync("tester", Guid.NewGuid(), Guid.NewGuid(), default))
            .Should().ThrowAsync<Translated>();
        await reconciliation.Invoking(r => r.NotePendingAsync(AVersion(), AVersion(), Guid.NewGuid(), SyncConflictSource.Push, default))
            .Should().ThrowAsync<Translated>();
    }

    [Fact]
    public void Current_versions_only_leaves_out_superseded_and_merged_in_versions_in_sql()
    {
        using var db = SqlServer();
        var sql = db.MachineTests.CurrentVersionsOnly(db).ToQueryString();

        sql.Should().Contain("NOT EXISTS").And.Contain("[SupersedesClientId]").And.Contain("[MergedFromClientId]");
    }

    [Fact]
    public void The_admin_lists_filters_translate_together()
    {
        using var db = SqlServer();
        var filter = new AdminTestQuery.Filter(
            Guid.NewGuid(), "tester", Guid.NewGuid(), "kowhai", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
            AdminTestQuery.Complete, HasConflicts: true, ShowDeleted: false);

        var sql = AdminTestQuery.Newest(AdminTestQuery.Apply(db, filter)).Skip(50).Take(50).ToQueryString();

        sql.Should().Contain("[SyncConflicts]").And.Contain("COALESCE").And.Contain("LIKE").And.Contain("[IsDeleted]")
            .And.Contain("ORDER BY").And.Contain("OFFSET");
    }

    [Fact]
    public async Task Flagging_a_pages_conflicted_tests_translates()
    {
        using var db = SqlServer();
        await db.Invoking(d => AdminTestQuery.ConflictedAsync(d, [AVersion()])).Should().ThrowAsync<Translated>();
    }

    [Fact]
    public void Administered_scope_translates_for_both_roles()
    {
        using var db = SqlServer();
        db.MachineTests.AdministeredBy(true, null).ToQueryString().Should().NotContain("[TestingCompanyId] =");
        db.MachineTests.AdministeredBy(false, Guid.NewGuid()).ToQueryString().Should().Contain("[TestingCompanyId] =");
        db.MachineTests.AdministeredBy(false, null).ToQueryString().Should().NotBeNullOrEmpty();
    }
}
