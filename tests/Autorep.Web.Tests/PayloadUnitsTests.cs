using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autorep.Web.Services;
using FluentAssertions;

namespace Autorep.Web.Tests;

// The server's view of "which fields changed" — what holds an administrator to their role's fields
// and works out what two colliding versions both changed. Client/versioning/merge.ts divides a payload
// into the same units.
public class PayloadUnitsTests
{
    private static JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Each_reading_recommendation_check_and_config_property_is_its_own_unit()
    {
        var before = P("""{"config":{"clusterCount":20,"hasAcr":false},"readings":{"tr.a":1,"tr.b":2},"recommendations":{"vp.wick":"Clean"},"visualFaults":{"vp.wick":{"status":"ok"}},"notes":"x","pulsatorRows":[{"id":"p1","unit":"1","values":{"rate":"60"}}]}""");
        var after = P("""{"config":{"clusterCount":24,"hasAcr":false},"readings":{"tr.a":1,"tr.b":3,"tr.c":4},"recommendations":{},"visualFaults":{"vp.wick":{"status":"fault","severity":"Minor"}},"notes":"x","pulsatorRows":[{"id":"p1","unit":"1","values":{"rate":"58"}}]}""");

        PayloadUnits.Changed(before, after).Should().Equal(
            "config.clusterCount", "pulsatorRows", "readings.tr.b", "readings.tr.c", "recommendations.vp.wick", "visualFaults.vp.wick");
    }

    [Fact]
    public void Bookkeeping_is_not_test_data()
    {
        var before = P("""{"id":"a","version":1,"syncState":"uploaded","updatedAt":"2026-01-01","amendments":[],"attestations":[],"currentStep":"Setup","notes":"same"}""");
        var after = P("""{"id":"b","version":2,"syncState":"local-only","updatedAt":"2026-02-02","amendments":[{"version":2}],"attestations":[{"step":"x"}],"currentStep":"ReviewSignOff","readonly":true,"notes":"same"}""");

        PayloadUnits.Changed(before, after).Should().BeEmpty();
    }

    [Fact]
    public void Absent_and_null_are_the_same_and_numbers_compare_by_value()
    {
        var before = P("""{"notes":null,"readings":{"tr.a":60},"config":{"herdSize":null}}""");
        var after = P("""{"readings":{"tr.a":60.0},"config":{}}""");

        PayloadUnits.Changed(before, after).Should().BeEmpty();
    }

    [Fact]
    public void An_attachment_is_the_same_attachment_with_or_without_its_bytes()
    {
        var withBytes = P("""{"pulsationPdf":{"name":"a.pdf","size":9,"attachedAt":"2026-10-01","base64":"JVBERi0="}}""");
        var pointer = P("""{"pulsationPdf":{"name":"a.pdf","size":9,"attachedAt":"2026-10-01","onServer":true}}""");
        var another = P("""{"pulsationPdf":{"name":"a.pdf","size":9,"attachedAt":"2026-10-05","base64":"JVBERi0="}}""");

        PayloadUnits.Changed(withBytes, pointer).Should().BeEmpty();
        PayloadUnits.Changed(withBytes, another).Should().Equal("pulsationPdf");
    }

    [Fact]
    public void Overlap_is_what_both_sides_changed_to_different_values_leaving_out_calculated_readings()
    {
        var @base = P("""{"notes":"a","nextTestDate":"2027-01-01","readings":{"tr.workingVacuum":48,"tr.regulationDeviation":-2},"recommendations":{"vp.wick":"Clean"}}""");
        var head = P("""{"notes":"admin","nextTestDate":"2027-02-02","readings":{"tr.workingVacuum":47,"tr.regulationDeviation":-3},"recommendations":{"vp.wick":"Clean"}}""");
        var incoming = P("""{"notes":"tester","nextTestDate":"2027-02-02","readings":{"tr.workingVacuum":46,"tr.regulationDeviation":-4},"recommendations":{"vp.wick":"Replace"}}""");

        PayloadUnits.Overlapping(@base, head, incoming).Should().Equal("notes", "readings.tr.workingVacuum");
    }

    [Fact]
    public void A_legacy_payload_is_recognised_as_one()
    {
        PayloadUnits.IsLegacy(P("""{"legacy":{"TestNo":1}}""")).Should().BeTrue();
        PayloadUnits.IsLegacy(P("""{"legacy":{},"currentStep":"Setup"}""")).Should().BeFalse("an adapted test is a LocalTest");
        PayloadUnits.IsLegacy(P("""{"currentStep":"Setup"}""")).Should().BeFalse();
    }

    // The server's list of calculated readings must be the wizard's: a reading the server didn't know
    // was calculated would be reported as a field both sides changed whenever its inputs changed.
    [Fact]
    public void The_calculated_readings_match_the_wizards_list()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Autorep.sln"))) root = Path.GetDirectoryName(root);
        root.Should().NotBeNull("the test runs from inside the repository");
        var source = File.ReadAllText(Path.Combine(root!, "src", "Autorep.Web", "Client", "passfail", "derived.ts"));

        var keys = Regex.Matches(source, @"\{\s*key:\s*""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        keys.Should().NotBeEmpty();
        keys.Should().BeEquivalentTo(DerivedReadings.Keys);
        source.Should().Contain($"key: `{DerivedReadings.OemCapacityPrefix}${{i}}`",
            "the per-pump OEM capacity keys are the one templated family");
    }
}
