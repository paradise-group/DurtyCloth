using System.IO;
using System.Text.Json;
using ModelContextProtocol;
using FooxClothStudio;
using FooxClothStudio.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

if (args.Length is not (1 or 3 or 5)) throw new ArgumentException("Usage: ClothMcpProbe server.dll [--verify resourceRoot | --audit folder | --inspect extractedPackage | --prepare resourceRoot extractedPackage newOutputParent | --photos resourceRoot updateReport newOutputRoot]");
var hostPath = Path.GetFullPath(args[0]);
var nativeHost = Path.GetExtension(hostPath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
{
    Name = "ParadiseClothMcpProbe", Command = nativeHost ? hostPath : "dotnet", Arguments = nativeHost ? [] : [hostPath]
}), new() { ClientInfo = new() { Name = "ClothMcpProbe", Version = "1.0.0" } });

async Task<CallToolResult> Call(string name, Dictionary<string, object?> arguments)
    => await client.CallToolAsync(name, arguments, cancellationToken: CancellationToken.None);

static JsonElement Data(CallToolResult result)
{
    var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    if (result.IsError == true) throw new InvalidDataException(text);
    return JsonDocument.Parse(text).RootElement.Clone();
}

var tools = await client.ListToolsAsync();
Check(tools.Select(tool => tool.Name).ToHashSet().SetEquals(new[] { "inspect_clothing_package", "inspect_clothing_resource", "validate_clothing_resource", "prepare_clothing_update", "export_clothing_photos", "audit_clothing_assets" }), "Unexpected MCP tools");
Console.WriteLine("PASS: MCP initialize, tool discovery and schemas.");

if (args.Length == 3 && args[1] == "--audit")
{
    var audit = Data(await Call("audit_clothing_assets", new() { ["root"] = args[2], ["limit"] = 25 }));
    Console.WriteLine($"Audit: {audit.GetProperty("filesChecked")} files, {audit.GetProperty("critical")} critical, {audit.GetProperty("warnings")} warnings, {audit.GetProperty("notices")} notices.");
    foreach (var issue in audit.GetProperty("issues").EnumerateArray())
        Console.WriteLine($"[{issue.GetProperty("severity").GetString()}] {issue.GetProperty("message").GetString()}");
    return;
}

if (args.Length == 3 && args[1] == "--inspect")
{
    var inspected = Data(await Call("inspect_clothing_package", new() { ["packageRoot"] = args[2] }));
    var severities = inspected.GetProperty("warnings").EnumerateArray().Select(issue => issue.GetProperty("severity").GetString()).ToArray();
    Console.WriteLine($"Package: {inspected.GetProperty("models")} models, {inspected.GetProperty("textures")} YTDs; {inspected.GetProperty("errors").GetArrayLength()} blockers, {severities.Count(severity => severity == "warning")} warnings, {severities.Count(severity => severity == "notice")} notices.");
    foreach (var error in inspected.GetProperty("errors").EnumerateArray().Take(10)) Console.WriteLine("[blocker] " + error.GetString());
    foreach (var warning in inspected.GetProperty("warnings").EnumerateArray().Take(10)) Console.WriteLine($"[{warning.GetProperty("severity").GetString()}] {warning.GetProperty("message").GetString()}");
    return;
}

if (args.Length == 3)
{
    Check(args[1] == "--verify", "Unknown action");
    var resource = Data(await Call("inspect_clothing_resource", new() { ["resourceRoot"] = args[2], ["limit"] = 1 }));
    var validation = Data(await Call("validate_clothing_resource", new() { ["resourceRoot"] = args[2] }));
    Check(resource.GetProperty("totalItems").GetInt32() == validation.GetProperty("identitiesChecked").GetInt32(), "Reimported item count differs from the persisted UUID map");
    Console.WriteLine($"PASS: reimported {resource.GetProperty("totalItems")} mapped items; {validation.GetProperty("ymtsChecked")} YMTs and {validation.GetProperty("shopsChecked")} shops inspected.");
    Console.WriteLine("Static diagnostics: " + validation.GetProperty("errors").GetRawText());
    return;
}

if (args.Length == 5)
{
    if (args[1] == "--photos")
    {
        using var report = JsonDocument.Parse(File.ReadAllText(args[3]));
        var ids = report.RootElement.GetProperty("additions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Concat(report.RootElement.TryGetProperty("reusedDrawables", out var reused)
                ? reused.EnumerateArray().Select(entry => entry.GetProperty("id").GetString()) : [])
            .Distinct().ToArray();
        var progress = new Progress<ProgressNotificationValue>(value => Console.WriteLine(value.Message));
        var result = await client.CallToolAsync("export_clothing_photos", new Dictionary<string, object?>
        { ["resourceRoot"] = args[2], ["itemIds"] = ids, ["outputRoot"] = args[4] }, progress, cancellationToken: CancellationToken.None);
        Console.WriteLine(Data(result).GetRawText());
        return;
    }
    Check(args[1] == "--prepare", "Unknown action");
    var package = Data(await Call("inspect_clothing_package", new() { ["packageRoot"] = args[3] }));
    Console.WriteLine($"Package: {package.GetProperty("models")} models, {package.GetProperty("textures")} YTDs; {package.GetProperty("errors").GetArrayLength()} blockers, {package.GetProperty("warnings").GetArrayLength()} warnings.");
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[3])!, "mcp-package-inspection.json"), package.GetRawText());
    var output = Data(await Call("prepare_clothing_update", new() { ["resourceRoot"] = args[2], ["packageRoot"] = args[3], ["outputParent"] = args[4] }));
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    var validation = Data(await Call("validate_clothing_resource", new() { ["resourceRoot"] = output.GetProperty("output").GetString() }));
    Console.WriteLine($"Validated {validation.GetProperty("ymtsChecked")} YMTs, {validation.GetProperty("shopsChecked")} shops, {validation.GetProperty("identitiesChecked")} UUIDs. Issues: {validation.GetProperty("errors").GetArrayLength()}.");
    return;
}

var fixture = Path.Combine(Path.GetTempPath(), "ParadiseClothStudio", "mcp-probes", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    var packageRoot = Path.Combine(fixture, "package");
    void Item(string relative)
    {
        var folder = Path.Combine(packageRoot, relative);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "jbib_002_u.ydd"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "jbib_diff_002_a_uni.ytd"), [4, 5, 6]);
    }
    Item("BOPE/M"); Item("PRF/M"); Item("BOPE/F");
    var inspection = Data(await Call("inspect_clothing_package", new() { ["packageRoot"] = packageRoot }));
    Check(inspection.GetProperty("errors").GetArrayLength() == 0, "Valid package structure rejected");
    var items = inspection.GetProperty("items").EnumerateArray().ToArray();
    Check(items.Length == 3 && items.Select(item => item.GetProperty("id").GetGuid()).Distinct().Count() == 3, "Repeated native filenames lost package identity");
    Check(items.Count(item => item.GetProperty("gender").GetString() == "Masculino") == 2, "M folder alias failed");
    Check(items.Count(item => item.GetProperty("gender").GetString() == "Feminino") == 1, "F folder alias failed");
    Check(items.All(item => item.GetProperty("texturePaths").GetArrayLength() == 1), "Textures crossed clothing folders");
    var repeated = Data(await Call("inspect_clothing_package", new() { ["packageRoot"] = packageRoot }));
    Check(inspection.GetRawText() == repeated.GetRawText(), "Package identities are not deterministic");
    Console.WriteLine("PASS: M/F folders, repeated names across departments, isolated textures and repeatable UUIDs.");

    Item("unknown");
    var unknown = Data(await Call("inspect_clothing_package", new() { ["packageRoot"] = packageRoot }));
    Check(unknown.GetProperty("errors").EnumerateArray().Any(error => error.GetString()!.Contains("Undefined gender")), "Unknown gender not reported");
    File.WriteAllBytes(Path.Combine(packageRoot, "orphan.ytd"), [1]);
    var orphan = ClothingOperations.InspectPackage(packageRoot);
    Check(orphan.UnassociatedAssets.Contains("orphan.ytd"), "Orphan texture was silently lost");
    Console.WriteLine("PASS: unknown gender and orphan asset diagnostics.");

    var source = Path.Combine(fixture, "source");
    var stream = Path.Combine(source, "stream");
    Directory.CreateDirectory(stream);
    File.WriteAllText(Path.Combine(source, "fxmanifest.lua"), "fx_version 'cerulean'\ngame 'gta5'\n");
    File.WriteAllBytes(Path.Combine(stream, "mp_m_freemode_01_original^jbib_000_u.ydd"), [1]);
    var resource = Data(await Call("inspect_clothing_resource", new() { ["resourceRoot"] = source, ["search"] = "original", ["limit"] = 1 }));
    Check(resource.GetProperty("matchedItems").GetInt32() == 1, "Resource search failed");
    var invalidPage = await Call("inspect_clothing_resource", new() { ["resourceRoot"] = source, ["limit"] = 0 });
    Check(invalidPage.IsError == true && invalidPage.Content.OfType<TextContentBlock>().Any(block => block.Text.Contains("between 1 and 200")), "Invalid pagination was not an actionable tool error");
    var missing = await Call("inspect_clothing_resource", new() { ["resourceRoot"] = Path.Combine(fixture, "missing") });
    Check(missing.IsError == true, "Missing resource was not a tool error");
    File.AppendAllText(Path.Combine(source, "fxmanifest.lua"), "data_file 'SHOP_PED_APPAREL_META_FILE' 'data/missing.meta'\n");
    var validation = Data(await Call("validate_clothing_resource", new() { ["resourceRoot"] = source }));
    Check(validation.GetProperty("errors").GetArrayLength() == 1, "Missing nested metadata was not detected");
    Console.WriteLine("PASS: resource pagination/search, protocol error results and nested metadata checks.");

    var existing = Path.Combine(fixture, "existing");
    Directory.CreateDirectory(existing);
    File.WriteAllText(Path.Combine(existing, "sentinel.txt"), "keep");
    var rejection = await Call("prepare_clothing_update", new() { ["resourceRoot"] = source, ["packageRoot"] = packageRoot, ["outputParent"] = existing });
    Check(rejection.IsError == true && File.ReadAllText(Path.Combine(existing, "sentinel.txt")) == "keep", "Existing output was changed");
    var nested = Path.Combine(source, "output");
    rejection = await Call("prepare_clothing_update", new() { ["resourceRoot"] = source, ["packageRoot"] = packageRoot, ["outputParent"] = nested });
    Check(rejection.IsError == true && !Directory.Exists(nested), "Nested output was created");
    var invalidOutput = Path.Combine(fixture, "invalid-output");
    var binaryPackage = Path.Combine(fixture, "bad-binaries");
    Directory.CreateDirectory(Path.Combine(binaryPackage, "male"));
    File.WriteAllBytes(Path.Combine(binaryPackage, "male", "jbib_000_u.ydd"), [1]);
    File.WriteAllBytes(Path.Combine(binaryPackage, "male", "jbib_diff_000_a_uni.ytd"), [2]);
    rejection = await Call("prepare_clothing_update", new() { ["resourceRoot"] = source, ["packageRoot"] = binaryPackage, ["outputParent"] = invalidOutput });
    Check(rejection.IsError == true && !Directory.Exists(invalidOutput), "Invalid binaries produced an output");
    Console.WriteLine("PASS: existing/nested output guards, invalid binary rejection and no partial output.");

    var photoOutput = Path.Combine(fixture, "photos");
    rejection = await Call("export_clothing_photos", new() { ["resourceRoot"] = source, ["itemIds"] = Array.Empty<string>(), ["outputRoot"] = photoOutput });
    Check(rejection.IsError == true && !Directory.Exists(photoOutput), "Empty photo selection produced an output");
    File.WriteAllBytes(Path.Combine(stream, "mp_m_freemode_01_original^jbib_diff_000_a_uni.ytd"), [2]);
    var photoProject = new ClothProject { Items = ClothCatalog.Import(Directory.EnumerateFiles(stream)) };
    ClothingIdentityMap.Write(OriginalResourceExporter.Describe(photoProject), source);
    rejection = await Call("export_clothing_photos", new() { ["resourceRoot"] = source,
        ["itemIds"] = new[] { Guid.NewGuid().ToString() }, ["outputRoot"] = photoOutput });
    Check(rejection.IsError == true && !Directory.Exists(photoOutput), "Unknown photo UUID produced an output");
    rejection = await Call("export_clothing_photos", new() { ["resourceRoot"] = source,
        ["itemIds"] = new[] { photoProject.Items[0].Id.ToString() }, ["outputRoot"] = photoOutput });
    Check(rejection.IsError == true && !Directory.Exists(photoOutput), "Failed photo render left partial files");
    Console.WriteLine("PASS: photo selection guards and STA render rollback.");
}
finally
{
    var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ParadiseClothStudio", "mcp-probes")) + Path.DirectorySeparatorChar;
    if (!Path.GetFullPath(fixture).StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected fixture path");
    Directory.Delete(fixture, true);
}
