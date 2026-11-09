using System.Reflection;
using System.Text.RegularExpressions;
using LazyBean.Application.Collector.Exceptions;
using LazyBean.Domain.Entities;
using Xunit;

namespace LazyBean.Application.Tests.Architecture;

// The rules that keep the layers apart. They read the project files, the source text and the compiled assemblies, so a
// change that breaks one of them fails the tests:
//   - the reference graph: Domain -> nothing; Application -> Domain; Infrastructure -> Application, Domain;
//     Api -> Application, Infrastructure; the desktop app -> Application, Infrastructure; four source projects in all;
//   - the application layer does not use HTTP client types, and does not touch DbContext, DbSet or Npgsql (its query
//     extensions over IQueryable stay, as agreed for this refactor);
//   - the raw external types (the Woo store and Meta search shapes) stay internal to Infrastructure;
//   - the desktop app never resolves the database: no DbContext, no AddPersistence, no connection string.
public class ArchitectureTests
{
    private static readonly string Root = FindRepositoryRoot();

    // What each source project may reference (by project name). Test projects are not part of the graph.
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["LazyBean.Domain"] = [],
        ["LazyBean.Application"] = ["LazyBean.Domain"],
        ["LazyBean.Infrastructure"] = ["LazyBean.Application", "LazyBean.Domain"],
        ["LazyBean.Api"] = ["LazyBean.Application", "LazyBean.Infrastructure"],
    };

    private static readonly string[] DesktopAllowedReferences = ["LazyBean.Application", "LazyBean.Infrastructure"];

    public static IEnumerable<object[]> SourceProjects() => AllowedReferences.Keys.Select(name => new object[] { name });

    [Fact]
    public void There_are_exactly_four_source_projects()
    {
        var slnx = File.ReadAllText(Path.Combine(Root, "backend", "LazyBean.slnx"));
        var projects = Regex.Matches(slnx, "Path=\"src/([^/]+)/")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedReferences.Keys.OrderBy(name => name, StringComparer.Ordinal), projects);
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void Each_source_project_references_only_what_the_graph_allows(string project)
    {
        var csproj = Path.Combine(Root, "backend", "src", project, project + ".csproj");
        Assert.Equal(AllowedReferences[project].OrderBy(x => x, StringComparer.Ordinal), ProjectReferencesOf(csproj));
    }

    [Fact]
    public void The_desktop_app_references_only_Application_and_Infrastructure()
    {
        var csproj = Path.Combine(Root, "desktop", "LazyBeanCollector", "LazyBeanCollector.csproj");
        Assert.Equal(
            DesktopAllowedReferences.OrderBy(x => x, StringComparer.Ordinal),
            ProjectReferencesOf(csproj));
    }

    [Fact]
    public void The_domain_assembly_references_no_other_LazyBean_assembly()
    {
        var referenced = typeof(Ad).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name != null && name.StartsWith("LazyBean", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(referenced);
    }

    [Fact]
    public void The_application_assembly_does_not_reference_Infrastructure_or_Api()
    {
        var referenced = typeof(CollectorApiException).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is "LazyBean.Infrastructure" or "LazyBean.Api")
            .ToArray();

        Assert.Empty(referenced);
    }

    [Fact]
    public void The_application_layer_uses_no_HTTP_client_types()
    {
        var offenders = ApplicationSources()
            .Where(file => Regex.IsMatch(Code(file), @"\bHttpClient\b|\bHttpRequestMessage\b|\bHttpResponseMessage\b|\bSystem\.Net\.Http\b"))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_application_layer_does_not_touch_DbContext_DbSet_or_Npgsql()
    {
        var offenders = ApplicationSources()
            .Where(file => Regex.IsMatch(Code(file), @"\bDbContext\b|\bLazyBeanDbContext\b|\bDbSet<|\bNpgsql"))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Raw_external_types_stay_inside_Infrastructure()
    {
        var infrastructure = typeof(LazyBean.Infrastructure.DependencyInjection).Assembly;

        // The Woo store's raw JSON shapes: only the adapter is public.
        var publicWoo = infrastructure.GetTypes()
            .Where(t => t.Namespace == "LazyBean.Infrastructure.External.Woo" && t.IsPublic && t.Name != "WooStoreApiAdapter")
            .Select(t => t.FullName)
            .ToArray();
        Assert.Empty(publicWoo);

        // Meta's raw search shapes and its session stay internal.
        var publicMeta = infrastructure.GetTypes()
            .Where(t => t.Namespace != null
                && t.Namespace.StartsWith("LazyBean.Infrastructure.External.Meta", StringComparison.Ordinal)
                && t.IsPublic
                && t.Name is "MetaSearchRequest" or "MetaSearchResponse" or "MetaAdsSession")
            .Select(t => t.FullName)
            .ToArray();
        Assert.Empty(publicMeta);
    }

    [Fact]
    public void The_desktop_host_never_resolves_the_database()
    {
        var offenders = Directory.GetFiles(Path.Combine(Root, "desktop", "LazyBeanCollector"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => Regex.IsMatch(
                Code(file),
                @"\bDbContext\b|\bLazyBeanDbContext\b|\bIDbContextFactory\b|\bAddPersistence\b|\bAddInfrastructure\b|\bConnectionStrings\b|\bNpgsql\b"))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> ApplicationSources() =>
        Directory.GetFiles(Path.Combine(Root, "backend", "src", "LazyBean.Application"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // The file's code without its line comments, so a word in a comment does not count.
    private static string Code(string file)
    {
        var lines = File.ReadAllLines(file).Select(line =>
        {
            var index = line.IndexOf("//", StringComparison.Ordinal);
            return index >= 0 ? line[..index] : line;
        });
        return string.Join('\n', lines);
    }

    private static string[] ProjectReferencesOf(string csproj)
    {
        var text = File.ReadAllText(csproj);
        return Regex.Matches(text, "ProjectReference Include=\"([^\"]+)\"")
            .Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "LazyBean.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root (backend/LazyBean.slnx) was not found above the test output.");
    }
}
