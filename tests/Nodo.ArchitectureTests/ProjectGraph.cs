using System.Xml.Linq;

namespace Nodo.ArchitectureTests;

/// <summary>
/// Reads the csproj files of the solution. Project/package references are checked from the project files
/// (not from compiled metadata) because the compiler drops unused references and most projects are still empty.
/// </summary>
internal sealed record ProjectInfo(string Name, IReadOnlySet<string> ProjectReferences, IReadOnlySet<string> PackageReferences);

internal static class ProjectGraph
{
    public static readonly string[] Modules = ["Quotes", "EventLifecycle", "HallSchedule"];

    private static readonly Lazy<IReadOnlyDictionary<string, ProjectInfo>> Cache = new(Load);

    public static IReadOnlyDictionary<string, ProjectInfo> Projects => Cache.Value;

    public static ProjectInfo Get(string name) => Projects[name];

    private static IReadOnlyDictionary<string, ProjectInfo> Load()
    {
        var root = FindRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(file =>
            {
                var doc = XDocument.Load(file);
                var projects = doc.Descendants("ProjectReference")
                    .Select(e => Path.GetFileNameWithoutExtension((string)e.Attribute("Include")!))
                    .ToHashSet();
                var packages = doc.Descendants("PackageReference")
                    .Select(e => (string)e.Attribute("Include")!)
                    .ToHashSet();
                return new ProjectInfo(Path.GetFileNameWithoutExtension(file), projects, packages);
            })
            .ToDictionary(p => p.Name);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nodo.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Nodo.slnx not found above the test binaries.");
    }
}
