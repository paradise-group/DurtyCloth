using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace FooxClothStudio.Mcp;

public static class ClothingOperations
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly SemaphoreSlim BuildGate = new(1, 1);
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ydd", ".ytd", ".yld" };

    public sealed record PackageInspection(string Root, int Models, int Textures, int Physics,
        List<ClothItem> Items, List<string> UnassociatedAssets, List<string> Errors, List<AssetBudgetIssue> Warnings);
    public sealed record ResourceValidation(string Root, int YmtsChecked, int ShopsChecked, int IdentitiesChecked, List<string> Errors);

    public static PackageInspection InspectPackage(string root, CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        var files = EnumerateFiles(root).Where(path => AssetExtensions.Contains(Path.GetExtension(path))).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var items = ClothCatalog.Import(files);
        var errors = new List<string>();
        if (items.Count == 0) errors.Add("No main YDD clothing models were found.");
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, item.ModelPath).Replace('\\', '/');
            if (item.Gender == "Indefinido")
            {
                var folder = Path.GetDirectoryName(relative)?.Replace('\\', '/').Split('/') ?? [];
                var alias = folder.Reverse().FirstOrDefault(part => part.Equals("m", StringComparison.OrdinalIgnoreCase) || part.Equals("f", StringComparison.OrdinalIgnoreCase));
                item.Gender = alias?.ToLowerInvariant() switch { "m" => "Masculino", "f" => "Feminino", _ => "Indefinido" };
            }
            // Package folder identity prevents identical native filenames from collapsing across departments.
            item.Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("paradise-clothing/package/v1/" + relative.ToLowerInvariant())).AsSpan(0, 16));
            item.TextureIds.Clear();
            ClothingIdentityMap.EnsureTextures(item);
            item.Name = relative;
            if (item.Gender == "Indefinido") errors.Add("Undefined gender: " + relative);
            if (item.Category == "Outros") errors.Add("Unknown clothing slot: " + relative);
            if (item.TexturePaths.Count > 26) errors.Add("More than 26 texture variants: " + relative);
            if (item.TexturePaths.Count == 0) errors.Add("No associated YTD: " + relative);
        }
        var associated = items.SelectMany(item => new[] { item.ModelPath, item.PhysicsPath }.Concat(item.TexturePaths).Concat(item.AlternateModelPaths))
            .Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphaned = files.Where(path => !associated.Contains(path)).Select(path => Path.GetRelativePath(root, path)).ToList();
        if (orphaned.Count > 0) errors.Add($"{orphaned.Count} unassociated assets; organize the package before building to avoid losing files.");
        var budget = InspectBudget(root, files, cancellationToken);
        errors.AddRange(budget.Where(issue => issue.Severity == AssetBudget.Critical).Select(issue => issue.Message));
        var warnings = budget.Where(issue => issue.Severity != AssetBudget.Critical)
            .OrderBy(issue => Array.IndexOf(SeverityOrder, issue.Severity)).ThenByDescending(issue => issue.MemoryBytes).ToList();
        return new(root, files.Count(path => Path.GetExtension(path).Equals(".ydd", StringComparison.OrdinalIgnoreCase)),
            files.Count(path => Path.GetExtension(path).Equals(".ytd", StringComparison.OrdinalIgnoreCase)),
            files.Count(path => Path.GetExtension(path).Equals(".yld", StringComparison.OrdinalIgnoreCase)), items, orphaned, errors, warnings);
    }

    public static object AuditAssets(string root, int limit, CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        var files = EnumerateFiles(root).Where(path => AssetExtensions.Contains(Path.GetExtension(path))).ToArray();
        var issues = InspectBudget(root, files, cancellationToken);
        return new
        {
            root, filesChecked = files.Length,
            critical = issues.Count(issue => issue.Severity == AssetBudget.Critical),
            warnings = issues.Count(issue => issue.Severity == AssetBudget.Warning),
            notices = issues.Count(issue => issue.Severity == AssetBudget.Notice),
            issues = issues.OrderBy(issue => Array.IndexOf(SeverityOrder, issue.Severity))
                .ThenByDescending(issue => issue.MemoryBytes).Take(Math.Clamp(limit, 1, 500)).ToArray()
        };
    }

    private static readonly string[] SeverityOrder = [AssetBudget.Critical, AssetBudget.Warning, AssetBudget.Notice];

    private static List<AssetBudgetIssue> InspectBudget(string root, string[] files, CancellationToken cancellationToken)
    {
        var issues = new List<AssetBudgetIssue>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            issues.AddRange(AssetBudget.Inspect(file, Path.GetRelativePath(root, file).Replace('\\', '/')));
        }
        return issues;
    }

    public static ClothProject OpenResource(string root, IProgress<string>? progress = null)
    {
        var resource = FiveMResource.Open(root);
        // Companions always share a slot; partition before the importer's model/texture matching.
        var groups = EnumerateFiles(Path.Combine(resource.Root, "stream"))
            .Where(path => AssetExtensions.Contains(Path.GetExtension(path))).GroupBy(path => ClothCatalog.ResolveType(path).Category);
        var items = new List<ClothItem>();
        foreach (var group in groups)
        {
            progress?.Report("Importing original clothing slot: " + group.Key);
            items.AddRange(ClothCatalog.Import(group));
        }
        ClothingIdentityMap.Restore(resource.Root, items);
        return new() { Name = Path.GetFileName(resource.Root), SourceResourceRoot = resource.Root, Items = items, UsePermanentClothingIds = true };
    }

    public static object InspectResource(string root, string search, int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "Use offset >= 0 and limit between 1 and 200.");
        var project = OpenResource(root);
        var document = OriginalResourceExporter.Describe(project);
        var matches = document.Entries.Where(entry => entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Collection.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new { root = project.SourceResourceRoot, totalItems = project.Items.Count,
            totalTextureFiles = project.Items.Sum(item => item.TexturePaths.Count),
            groups = document.Entries.GroupBy(entry => (entry.Gender, entry.Kind, entry.Slot)).Select(group => new { group.Key.Gender, group.Key.Kind, group.Key.Slot, count = group.Count() }),
            matchedItems = matches.Length, offset, limit, items = matches.Skip(offset).Take(limit) };
    }

    public static ResourceValidation ValidateResource(string root, CancellationToken cancellationToken = default)
    {
        root = FiveMResource.Open(root).Root;
        var errors = new List<string>();
        var files = EnumerateFiles(root).ToArray();
        var byName = files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var manifest = File.ReadAllText(Path.Combine(root, "fxmanifest.lua"));
        foreach (Match declaration in Regex.Matches(manifest, @"(?m)^\s*data_file\s+['""][^'""]+['""]\s+['""]([^'""]+)['""]"))
        {
            var relative = declaration.Groups[1].Value;
            if (relative.IndexOfAny(['*', '?']) >= 0) { errors.Add("Wildcard data_file requires manual verification: " + relative); continue; }
            var declared = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsWithin(declared, root) || !File.Exists(declared)) errors.Add("Missing or external declared metadata: " + relative);
        }
        var ymts = 0;
        var shops = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Path.GetExtension(file).Equals(".ymt", StringComparison.OrdinalIgnoreCase))
                {
                    ymts++;
                    var ymt = new YmtFile(); ymt.Load(File.ReadAllBytes(file));
                    if (ymt.Meta is null) throw new InvalidDataException("YMT has no readable metadata.");
                    XDocument.Parse(MetaXml.GetXml(ymt.Meta));
                }
                if (Path.GetExtension(file).Equals(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    var xml = XDocument.Load(file);
                    if (xml.Root?.Name.LocalName != "ShopPedApparel") continue;
                    shops++;
                    var collection = xml.Root.Element("fullDlcName")?.Value;
                    if (string.IsNullOrWhiteSpace(collection) || !byName.ContainsKey(collection + ".ymt"))
                        errors.Add("Shop collection YMT missing: " + Path.GetRelativePath(root, file));
                }
            }
            catch (Exception error) { errors.Add(Path.GetRelativePath(root, file) + ": " + error.Message); }
        }
        var identities = 0;
        var mapPath = Path.Combine(root, ClothingIdentityMap.FileName);
        if (File.Exists(mapPath))
        {
            try
            {
                var map = ClothingIdentityMap.Read(mapPath);
                ClothingIdentityMap.Validate(map);
                identities = map.Entries.Count;
                foreach (var entry in map.Entries)
                foreach (var name in new[] { entry.ModelFile }.Concat(entry.Textures.Select(texture => texture.File)).Where(name => name is not null))
                    if (!byName.ContainsKey(name!)) errors.Add("Mapped asset missing: " + name);
            }
            catch (Exception error) { errors.Add("Identity map: " + error.Message); }
        }
        return new(root, ymts, shops, identities, errors);
    }

    public static async Task<object> PrepareUpdate(string resourceRoot, string packageRoot, string outputParent,
        string? gameFolder = null, CancellationToken cancellationToken = default, IProgress<string>? progress = null,
        bool appendToLatestCollection = true)
    {
        await BuildGate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => Build(resourceRoot, packageRoot, outputParent, gameFolder, appendToLatestCollection, cancellationToken, progress), cancellationToken); }
        finally { BuildGate.Release(); }
    }

    private static object Build(string resourceRoot, string packageRoot, string outputParent, string? gameFolder,
        bool appendToLatestCollection, CancellationToken cancellationToken, IProgress<string>? progress)
    {
        resourceRoot = Path.GetFullPath(resourceRoot);
        packageRoot = Path.GetFullPath(packageRoot);
        outputParent = Path.GetFullPath(outputParent);
        if (Directory.Exists(outputParent) || File.Exists(outputParent)) throw new IOException("Output folder already exists: " + outputParent);
        if (IsWithin(outputParent, resourceRoot) || IsWithin(resourceRoot, outputParent) || IsWithin(outputParent, packageRoot) || IsWithin(packageRoot, outputParent))
            throw new IOException("Output must be outside both inputs and cannot contain either input.");
        RejectLinkedAncestors(outputParent);
        var package = InspectPackage(packageRoot, cancellationToken);
        if (package.Errors.Count > 0) throw new InvalidDataException(string.Join("\n", package.Errors));
        foreach (var item in package.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Validating package clothing: " + item.Name);
            ModelPreview.Read(item.ModelPath, false);
            foreach (var texture in item.TexturePaths) TextureContent.Fingerprint(texture);
            foreach (var model in item.AlternateModelPaths) ModelPreview.Read(model, false);
            if (item.PhysicsPath is not null) YldInspector.CountClothes(item.PhysicsPath);
        }
        var project = OpenResource(resourceRoot, progress);
        var original = OriginalResourceExporter.Describe(project);
        var knownIds = original.Entries.Select(entry => entry.Id).ToHashSet();
        if (package.Items.Any(item => knownIds.Contains(item.Id))) throw new InvalidDataException("This package already has item IDs in the source resource; inspect before importing again.");
        project.Items.AddRange(package.Items);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputParent);
        var ownershipFile = Path.Combine(outputParent, ".mcp-build.lock");
        // CreateNew claims the staging folder even if two MCP processes passed the existence check together.
        using (new FileStream(ownershipFile, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            // The existing exporter owns rollback and verifies all copied original files by SHA-256.
            var output = ResourceUpdateExporter.Export(project, Path.GetFullPath, resourceRoot, outputParent,
                new BuildProgress(progress), gameFolder ?? GamePedAssets.FindGameFolder(), appendToLatestCollection);
            var map = ClothingIdentityMap.Read(Path.Combine(output, ClothingIdentityMap.FileName));
            foreach (var entry in original.Entries)
                if (JsonSerializer.Serialize(map.Entries.Single(current => current.Id == entry.Id)) != JsonSerializer.Serialize(entry))
                    throw new InvalidDataException("An original UUID or native address changed: " + entry.Name);
            var validation = ValidateResource(output);
            var baseline = ValidateResource(resourceRoot);
            var newErrors = validation.Errors.Except(baseline.Errors, StringComparer.Ordinal).ToArray();
            if (newErrors.Length > 0) throw new InvalidDataException(string.Join("\n", newErrors));
            var addedIds = package.Items.Select(item => item.Id).ToHashSet();
            var added = map.Entries.Where(entry => addedIds.Contains(entry.Id)).ToArray();
            var originalCollections = original.Entries.Select(entry => (entry.Model, entry.Collection)).ToHashSet();
            object[] Collections(bool existing) => added.Where(entry => originalCollections.Contains((entry.Model, entry.Collection)) == existing)
                .GroupBy(entry => new { entry.Collection, entry.Gender }).OrderBy(group => group.Key.Collection, StringComparer.Ordinal)
                .Select(group => (object)new { collection = group.Key.Collection, gender = group.Key.Gender, items = group.Count() }).ToArray();
            if (added.Length != package.Items.Count || map.Entries.Count != original.Entries.Count + added.Length)
                throw new InvalidDataException("The output identity count does not match the planned update.");
            var report = new { sourceRoot = resourceRoot, packageRoot, output, originalItems = original.Entries.Count,
                addedItems = added.Length, addedTextureFiles = package.Textures, originalIdsAndAddressesPreserved = true,
                originalFilesHashVerified = true, baselineValidationErrors = baseline.Errors, validation,
                addedCollections = Collections(false), appendedCollections = Collections(true),
                packageWarnings = package.Warnings, additions = added, deployment = "Prepared only. Faction wardrobe configuration and in-game testing are separate steps." };
            var reportPath = Path.Combine(outputParent, "update-report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
            var projectPath = Path.Combine(outputParent, "update.fcsproj");
            File.WriteAllText(projectPath, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
            File.Delete(ownershipFile);
            return new { output, projectPath, reportPath, originalItems = original.Entries.Count, addedItems = added.Length,
                addedTextureFiles = package.Textures, packageWarnings = package.Warnings.Count(issue => issue.Severity == AssetBudget.Warning),
                packageNotices = package.Warnings.Count(issue => issue.Severity == AssetBudget.Notice), baselineValidationErrors = baseline.Errors };
        }
        catch
        {
            if (Directory.Exists(outputParent)) Directory.Delete(outputParent, true);
            throw;
        }
    }

    private static bool IsWithin(string path, string root) => Path.GetFullPath(path).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectLinkedAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Linked directories are not supported: " + directory.FullName);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        RejectLinkedAncestors(root);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked files or directories are not supported: " + entry);
            if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            else yield return Path.GetFullPath(entry);
        }
    }

    private sealed class BuildProgress(IProgress<string>? destination) : IProgress<string>
    {
        private int verified;
        public void Report(string value)
        {
            if (!value.StartsWith("Verifying original assets:", StringComparison.Ordinal) || ++verified % 1000 == 0)
            {
                Console.Error.WriteLine(value);
                destination?.Report(value);
            }
        }
    }
}
