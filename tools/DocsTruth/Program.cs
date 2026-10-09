using System.Xml.Linq;

namespace NexJob.DocsTruth;

/// <summary>Entry point of the docs-truth tool.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var strict = args.Contains("--strict", StringComparer.Ordinal);
        var root = ValueOf(args, "--root") ?? FindRoot(Directory.GetCurrentDirectory());
        if (root is null)
        {
            Console.Error.WriteLine("Run the tool inside the repository or pass --root <path>.");
            return 2;
        }

        var version = ValueOf(args, "--version") ?? ReadVersion(root);
        if (!Version.TryParse(version, out var parsed))
        {
            Console.Error.WriteLine($"Could not read a version ('{version}'); pass --version x.y.z.");
            return 2;
        }

        var findings = DocsTruthRunner.Run(root, parsed);
        var report = Report.Render(findings);
        Console.WriteLine(report);
        if (ValueOf(args, "--report") is { } reportPath)
        {
            File.WriteAllText(reportPath, report);
        }

        return Report.ExitCode(findings, strict);
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NexJob.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string? ReadVersion(string root)
    {
        var props = Path.Combine(root, "Directory.Build.props");
        return File.Exists(props) ? XDocument.Load(props).Descendants("VersionPrefix").FirstOrDefault()?.Value : null;
    }
}
