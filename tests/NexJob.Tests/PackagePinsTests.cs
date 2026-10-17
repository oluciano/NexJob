using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

/// <summary>The packages target net8.0 and net10.0, and the security pins stay in the net8.0 dependency group only (#582).</summary>
[Collection("PackagePins")]
public sealed class PackagePinsTests : IClassFixture<PackagePinsTests.PackedPackages>
{
    // The explicit pins of the current release (security overrides of transitive versions), by package project.
    private static readonly Dictionary<string, string[]> ExpectedPins = new(StringComparer.Ordinal)
    {
        ["NexJob"] = ["System.Diagnostics.DiagnosticSource:8.0.1", "System.Text.Encodings.Web:8.0.0"],
        ["NexJob.Postgres"] = ["System.Text.Encodings.Web:8.0.0"],
        ["NexJob.Trigger.AzureServiceBus"] = ["System.Text.Json:8.0.5"],
    };

    private readonly PackedPackages _packed;

    public PackagePinsTests(PackedPackages packed) => _packed = packed;

    public static TheoryData<string> Projects => new(ExpectedPins.Keys.ToArray());

    [Theory]
    [MemberData(nameof(Projects))]
    public void Nuspec_KeepsEveryPinInTheNet8Group_AndNoneInNet10(string project)
    {
        // N1: the net8.0 group has the pins of the current release, with their versions; the net10.0 group has none of them.
        var groups = Groups(_packed.Nuspec(project));

        MissingPins(groups["net8.0"], ExpectedPins[project]).Should().BeEmpty();
        var pinnedIds = ExpectedPins[project].Select(p => p.Split(':')[0]);
        groups["net10.0"].Keys.Should().NotContain(pinnedIds);
    }

    [Fact]
    public void MissingPins_ReportsAPinDroppedFromTheGroup()
    {
        // N2: a pin dropped from the group (or lowered) is reported.
        var group = new Dictionary<string, string>(StringComparer.Ordinal) { ["System.Text.Encodings.Web"] = "[8.0.0, )" };

        MissingPins(group, ["System.Text.Encodings.Web:8.0.0", "System.Diagnostics.DiagnosticSource:8.0.1"])
            .Should().Equal("System.Diagnostics.DiagnosticSource:8.0.1");
        MissingPins(group, ["System.Text.Encodings.Web:8.0.1"]).Should().Equal("System.Text.Encodings.Web:8.0.1");
    }

    [Fact]
    public void MissingPins_WithNoPinsExpected_ReportsNothing()
    {
        // N3 (boundary): an empty expectation and an empty group are fine.
        MissingPins(new Dictionary<string, string>(StringComparer.Ordinal), []).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Nupkg_HoldsTheLibrariesOfBothFrameworks(string project)
    {
        // N3: the package carries lib/net8.0 and lib/net10.0.
        var entries = _packed.Entries(project);

        entries.Should().Contain($"lib/net8.0/{project}.dll");
        entries.Should().Contain($"lib/net10.0/{project}.dll");
    }

    private static IEnumerable<string> MissingPins(IReadOnlyDictionary<string, string> group, IEnumerable<string> expected) =>
        expected.Where(pin =>
        {
            var parts = pin.Split(':');
            return !group.TryGetValue(parts[0], out var range) || range.Trim('[', ' ', ',', ')') != parts[1];
        });

    private static Dictionary<string, Dictionary<string, string>> Groups(XDocument nuspec)
    {
        XNamespace ns = nuspec.Root!.Name.Namespace;
        return nuspec.Descendants(ns + "group")
            .ToDictionary(
                g => (string)g.Attribute("targetFramework")!,
                g => g.Elements(ns + "dependency").ToDictionary(d => (string)d.Attribute("id")!, d => (string)d.Attribute("version")!, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    /// <summary>Packs the sample projects once and exposes their nuspec and file list.</summary>
    public sealed class PackedPackages : IDisposable
    {
        private readonly string _output = Path.Combine(Path.GetTempPath(), "nexjob-pack-" + Guid.NewGuid().ToString("N"));

        public PackedPackages()
        {
            Directory.CreateDirectory(_output);
            var root = FindRepoRoot();
            foreach (var project in ExpectedPins.Keys)
            {
                var start = new ProcessStartInfo("dotnet", $"pack \"{Path.Combine(root, "src", project, project + ".csproj")}\" -c Release -o \"{_output}\" -p:MinVerVersionOverride=0.0.0-test")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                process.ExitCode.Should().Be(0, output);
            }
        }

        public XDocument Nuspec(string project)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(_output, project + ".0.0.0-test.nupkg"));
            using var stream = zip.GetEntry(project + ".nuspec")!.Open();
            return XDocument.Load(stream);
        }

        public IReadOnlyList<string> Entries(string project)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(_output, project + ".0.0.0-test.nupkg"));
            return zip.Entries.Select(e => e.FullName).ToList();
        }

        public void Dispose() => Directory.Delete(_output, recursive: true);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexJob.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("NexJob.sln not found above the test binaries.");
        }
    }
}
