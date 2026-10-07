using Autorep.Web.Services.Pdfs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Autorep.Web.Tests;

// The contract every PDF store keeps — the in-memory one the tests and E2E run on, and the folder
// one Development runs on. The Blob store keeps it against a real account, which only staging has
// (CI holds no Azure credentials for tests); it shares the hash and key rules exercised here.
public abstract class PdfStoreContract
{
    protected abstract IPdfStore Store { get; }

    private static readonly byte[] Report = "%PDF-1.7 the report as signed off"u8.ToArray();
    private static readonly byte[] Revised = "%PDF-1.7 a different report"u8.ToArray();
    private const string Key = "tester-1/6f1c1d1e-0000-4000-8000-000000000001/final-report.pdf";

    [Fact]
    public async Task Gives_back_what_was_put_with_its_hash_and_type()
    {
        var put = await Store.PutAsync(PdfContainer.FinalReports, Key, Report, "application/pdf");

        put.Outcome.Should().Be(PdfPutOutcome.Created);
        put.Sha256.Should().Be(PdfHash.Sha256Hex(Report));
        var got = await Store.GetAsync(PdfContainer.FinalReports, Key);
        got!.Bytes.Should().Equal(Report);
        got.Info.Should().Be(new StoredPdfInfo(PdfHash.Sha256Hex(Report), Report.Length, "application/pdf"));
        (await Store.ExistsAsync(PdfContainer.FinalReports, Key)).Should().BeTrue();
    }

    [Fact]
    public async Task Putting_the_same_bytes_again_is_a_no_op_and_different_bytes_replace_them()
    {
        await Store.PutAsync(PdfContainer.FinalReports, Key, Report, "application/pdf");

        (await Store.PutAsync(PdfContainer.FinalReports, Key, Report, "application/pdf")).Outcome.Should().Be(PdfPutOutcome.Unchanged);
        (await Store.PutAsync(PdfContainer.FinalReports, Key, Revised, "application/pdf")).Outcome.Should().Be(PdfPutOutcome.Replaced);
        (await Store.GetAsync(PdfContainer.FinalReports, Key))!.Bytes.Should().Equal(Revised);
    }

    [Fact]
    public async Task Containers_are_separate_and_a_missing_key_is_null_not_an_error()
    {
        await Store.PutAsync(PdfContainer.FinalReports, Key, Report, "application/pdf");

        (await Store.GetAsync(PdfContainer.PulsationData, Key)).Should().BeNull();
        (await Store.GetInfoAsync(PdfContainer.PulsationData, Key)).Should().BeNull();
        (await Store.GetAsync(PdfContainer.FinalReports, "tester-1/nothing-here.pdf")).Should().BeNull();
    }

    [Fact]
    public async Task Info_says_what_is_held_without_reading_it()
    {
        await Store.PutAsync(PdfContainer.PulsationData, Key, Report, "application/pdf");

        var info = await Store.GetInfoAsync(PdfContainer.PulsationData, Key);

        info.Should().Be(new StoredPdfInfo(PdfHash.Sha256Hex(Report), Report.Length, "application/pdf"));
    }

    [Fact]
    public async Task Delete_removes_it_once()
    {
        await Store.PutAsync(PdfContainer.FinalReports, Key, Report, "application/pdf");

        (await Store.DeleteAsync(PdfContainer.FinalReports, Key)).Should().BeTrue();
        (await Store.DeleteAsync(PdfContainer.FinalReports, Key)).Should().BeFalse();
        (await Store.GetAsync(PdfContainer.FinalReports, Key)).Should().BeNull();
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("tester-1/../../escape.pdf")]
    [InlineData("tester-1//final-report.pdf")]
    [InlineData("/absolute.pdf")]
    [InlineData("tester 1/final-report.pdf")]
    [InlineData("")]
    public async Task Refuses_a_key_that_could_leave_its_container(string key)
    {
        var put = () => Store.PutAsync(PdfContainer.FinalReports, key, Report, "application/pdf");
        await put.Should().ThrowAsync<ArgumentException>();
    }
}

public class InMemoryPdfStoreTests : PdfStoreContract
{
    private readonly InMemoryPdfStore _store = new();
    protected override IPdfStore Store => _store;

    [Fact]
    public async Task Unavailable_fails_every_call_as_an_unreachable_account_would()
    {
        _store.Unavailable = true;
        var get = () => _store.GetAsync(PdfContainer.FinalReports, "tester-1/a.pdf");
        await get.Should().ThrowAsync<PdfStoreException>();
    }
}

public sealed class LocalFolderPdfStoreTests : PdfStoreContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autorep-pdf-store-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFolderPdfStore _store;
    protected override IPdfStore Store => _store;

    public LocalFolderPdfStoreTests() =>
        _store = new LocalFolderPdfStore(_root, new Dictionary<PdfContainer, string>
        {
            [PdfContainer.FinalReports] = "final-reports",
            [PdfContainer.PulsationData] = "pulsation-data",
        });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Lays_the_key_out_as_a_path_under_the_containers_folder()
    {
        await _store.PutAsync(PdfContainer.PulsationData, "tester-9/abc/pulsation/x.pdf", "%PDF-1"u8.ToArray(), "application/pdf");

        File.Exists(Path.Combine(_root, "pulsation-data", "tester-9", "abc", "pulsation", "x.pdf")).Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_to_hand_back_a_file_that_no_longer_matches_its_hash()
    {
        const string key = "tester-1/abc/final-report.pdf";
        await _store.PutAsync(PdfContainer.FinalReports, key, "%PDF-1 original"u8.ToArray(), "application/pdf");
        await File.WriteAllTextAsync(Path.Combine(_root, "final-reports", "tester-1", "abc", "final-report.pdf"), "%PDF-1 tampered");

        var get = () => _store.GetAsync(PdfContainer.FinalReports, key);

        await get.Should().ThrowAsync<PdfStoreException>();
    }
}

public class PdfKeysTests
{
    [Fact]
    public void Keys_are_deterministic_and_under_the_testers_prefix()
    {
        var client = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var sha = new string('a', 64);

        PdfKeys.FinalReport("4b0e7f61-1c2d-4e5f-8a9b-0c1d2e3f4a5b", client)
            .Should().Be("4b0e7f61-1c2d-4e5f-8a9b-0c1d2e3f4a5b/0f8fad5b-d9cb-469f-a165-70867728950e/final-report.pdf");
        PdfKeys.Pulsation("tester-1", client, sha)
            .Should().Be($"tester-1/0f8fad5b-d9cb-469f-a165-70867728950e/pulsation/{sha}.pdf");
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_tester_id_that_could_change_the_path_is_refused(string testerId)
    {
        var make = () => PdfKeys.FinalReport(testerId, Guid.NewGuid());
        make.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_pulsation_key_needs_a_real_sha256()
    {
        var make = () => PdfKeys.Pulsation("tester-1", Guid.NewGuid(), "not-a-hash");
        make.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Recognises_a_pdf_by_its_header_only()
    {
        PdfHash.LooksLikePdf("%PDF-1.7\n..."u8).Should().BeTrue();
        PdfHash.LooksLikePdf("<!DOCTYPE html>"u8).Should().BeFalse();
        PdfHash.LooksLikePdf(ReadOnlySpan<byte>.Empty).Should().BeFalse();
    }
}

// Which store each environment gets, and that a shared environment can't start without Blob
// Storage — the failure mode it prevents is reports kept on an App Service's disk and lost.
public class PdfStoreRegistrationTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Autorep.Web";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Theory]
    [InlineData("Testing", PdfStoreProvider.InMemory)]
    [InlineData("Development", PdfStoreProvider.LocalFolder)]
    public void Tests_and_development_need_no_Azure(string environment, PdfStoreProvider expected) =>
        PdfStoreRegistration.Choose(Config(), new Env(environment)).Should().Be(expected);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_shared_environment_uses_Blob_Storage(string environment) =>
        PdfStoreRegistration.Choose(Config(("AzureStorage:AccountName", "stnzmptaautorep")), new Env(environment))
            .Should().Be(PdfStoreProvider.Blob);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_shared_environment_without_an_account_name_stops_at_startup(string environment)
    {
        var choose = () => PdfStoreRegistration.Choose(Config(("AzureStorage:AccountName", "")), new Env(environment));
        choose.Should().Throw<InvalidOperationException>().WithMessage("*AzureStorage:AccountName*");
    }

    [Fact]
    public void Production_ignores_an_attempt_to_switch_the_store_off_Blob()
    {
        var choose = () => PdfStoreRegistration.Choose(Config(("PdfStore:Provider", "LocalFolder")), new Env("Production"));
        choose.Should().Throw<InvalidOperationException>("Production is Blob whatever the setting says, so the missing account still stops it");
    }

    [Fact]
    public void Elsewhere_the_store_can_be_chosen()
    {
        PdfStoreRegistration.Choose(Config(("PdfStore:Provider", "InMemory")), new Env("Development"))
            .Should().Be(PdfStoreProvider.InMemory);
    }

    [Fact]
    public void Container_names_come_from_the_settings_the_Bicep_writes()
    {
        var containers = PdfStoreRegistration.Containers(Config(
            ("AzureStorage:FinalReportsContainer", "final-reports"), ("AzureStorage:PulsationDataContainer", "pulsation-data")));

        containers[PdfContainer.FinalReports].Should().Be("final-reports");
        containers[PdfContainer.PulsationData].Should().Be("pulsation-data");
    }
}
