using System.Text.RegularExpressions;
using Nodo.Api.Invariants;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>docs/invariants.md is the traceability matrix: it must not drift from the catalog the API publishes.</summary>
public partial class InvariantDocumentationTests
{
    private static readonly string Root = FindRoot();

    private sealed record Row(string Id, string Owner, string Code, string Tests, string Database, string Probe);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nodo.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Nodo.slnx not found above the test binaries.");
    }

    private static List<Row> ReadMatrix()
    {
        var rows = new List<Row>();
        foreach (var line in File.ReadAllLines(Path.Combine(Root, "docs", "invariants.md")))
        {
            if (!RowStart().IsMatch(line))
                continue;
            var cells = line.Split('|').Select(c => c.Trim()).ToArray(); // '', id, rule, owner, code, tests, db, probe, ''
            rows.Add(new Row(cells[1], cells[3], cells[4], cells[5], cells[6], cells[7]));
        }
        return rows;
    }

    [GeneratedRegex(@"^\|\s*INV-\d\d\s*\|")]
    private static partial Regex RowStart();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Backticked();

    [Fact]
    public void Matrix_HasOneRowForEachOfTheCatalogInvariants() =>
        ReadMatrix().Select(r => r.Id).ShouldBe(InvariantCatalog.All.Select(i => i.Id));

    [Fact]
    public void Matrix_CodesMatchTheCatalog()
    {
        foreach (var row in ReadMatrix())
        {
            var invariant = InvariantCatalog.Find(row.Id)!;
            var documented = invariant.ByDesign
                ? ["(por diseño)"]
                : Backticked().Matches(row.Code).Select(m => m.Groups[1].Value).ToArray();

            documented.ShouldBe(invariant.Code.Split(" / "), row.Id);
        }
    }

    [Fact]
    public void Matrix_DatabaseColumnAgreesWithTheCatalogAndNamesTheRightInvariant()
    {
        foreach (var row in ReadMatrix())
        {
            var invariant = InvariantCatalog.Find(row.Id)!;
            var constraints = Backticked().Matches(row.Database).Select(m => m.Groups[1].Value.Split(' ')[0]).ToList();

            (constraints.Count > 0).ShouldBe(invariant.DatabaseEnforced, $"{row.Id}: BD column vs catalog");
            // the id travels in the constraint name: ck_..._inv04_..., ex_..._inv18_..., ux_..._inv19_...
            var marker = $"_inv{row.Id.Substring(4)}_";
            constraints.ShouldAllBe(c => c.Contains(marker), row.Id);
        }
    }

    [Fact]
    public void Matrix_EveryTestClassItCitesExists()
    {
        var testSources = Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();

        foreach (var row in ReadMatrix())
        {
            foreach (var cited in Backticked().Matches(row.Tests).Select(m => m.Groups[1].Value).Where(t => t.EndsWith("Tests")))
            {
                testSources.ShouldContain(s => Regex.IsMatch(s, $@"class\s+{cited}\b"), $"{row.Id} cites {cited}, which does not exist");
            }
        }
    }

    [Fact]
    public void Matrix_EveryInvariantHasANonEmptyOwnerAndProbeDescription()
    {
        foreach (var row in ReadMatrix())
        {
            row.Owner.ShouldNotBeNullOrWhiteSpace(row.Id);
            row.Probe.ShouldNotBeNullOrWhiteSpace(row.Id);
            row.Tests.ShouldNotBeNullOrWhiteSpace(row.Id);
        }
    }
}
