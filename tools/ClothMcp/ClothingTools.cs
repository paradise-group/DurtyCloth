using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FooxClothStudio.Mcp;

[McpServerToolType]
public static class ClothingTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string Invoke(Func<object> operation)
    {
        try { return Json(operation()); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new McpException(error.Message, error); }
    }

    [McpServerTool(Name = "inspect_clothing_package", ReadOnly = true, OpenWorld = false)]
    [Description("Inspect an extracted local YDD/YTD/YLD clothing package without changing files. Recognizes M/F and male/female folders, keeps repeated filenames in different folders separate, and reports unknown genders, missing/orphan textures and limits. Also checks the asset budget: errors list textures that would crash the client (over 64 MB of memory), and warnings list each texture above 2048 or over 16 MB of memory (severity warning) and each smaller texture that is uncompressed or has no mipmaps (severity notice), with resolution, format and MB. Extract archives before calling. Does not download or execute package contents.")]
    public static string InspectPackage(string packageRoot, CancellationToken cancellationToken = default)
        => Invoke(() => ClothingOperations.InspectPackage(packageRoot, cancellationToken));

    [McpServerTool(Name = "audit_clothing_assets", ReadOnly = true, OpenWorld = false)]
    [Description("Scan every YTD/YDD/YLD under a local folder (an extracted package, a stream folder or a whole resource) against the asset budget without changing files. Reports textures that would crash the client (over 64 MB of memory, ERR_STR_FAILURE_3) as critical, textures above 2048 or over 16 MB of memory as warnings, and smaller textures that are uncompressed or have no mipmaps as notices, each with resolution, format, mip levels and MB, heaviest first. The standard is 1024, or 2048 when 1024 loses needed detail. A whole resource can take minutes.")]
    public static string AuditAssets(string root, int limit = 100, CancellationToken cancellationToken = default)
        => Invoke(() => ClothingOperations.AuditAssets(root, limit, cancellationToken));

    [McpServerTool(Name = "inspect_clothing_resource", ReadOnly = true, OpenWorld = false)]
    [Description("Inspect a local FiveM resource using the studio importer and persistent UUID map. Returns counts and a paginated clothing list; search matches labels, filenames and collections. Does not control the open GUI or connect to FXServer.")]
    public static string InspectResource(string resourceRoot, string search = "", int offset = 0, int limit = 50)
        => Invoke(() => ClothingOperations.InspectResource(resourceRoot, search, offset, limit));

    [McpServerTool(Name = "validate_clothing_resource", ReadOnly = true, OpenWorld = false)]
    [Description("Perform static checks on a local FiveM resource, including nested metadata: literal data_file paths, readable YMTs, shop collection references, UUID uniqueness and mapped asset presence. This does not prove loading or appearance in game.")]
    public static string ValidateResource(string resourceRoot, CancellationToken cancellationToken = default)
        => Invoke(() => ClothingOperations.ValidateResource(resourceRoot, cancellationToken));

    [McpServerTool(Name = "prepare_clothing_update", Destructive = false, OpenWorld = false)]
    [Description("Build a new reviewable copy of a FiveM clothing resource with every item from an extracted package added. First reuses byte-identical component models of the same gender/slot only if the entire incoming texture batch fits the 26 A-Z positions; otherwise keeps the whole batch in a new drawable. Published drawables are never consolidated. By default new components are appended after the last native index of the latest pcs collection of each gender (no index shifts, no new collection); a new collection per gender is created only when there is no pcs collection yet, a slot would exceed 128 drawables, or the item is a prop or needs companion metadata. appendToLatestCollection=false creates new collections for drawables that could not be reused. Reuses the studio exporter, preserves original files, native indices and UUIDs, validates package binaries, and writes a project and per-item mapping report. outputParent must not exist and must be outside both inputs. Never installs, overwrites, restarts or changes faction wardrobe scripts. Can copy many GB; allow time for hash verification.")]
    public static async Task<string> PrepareUpdate(string resourceRoot, string packageRoot, string outputParent, IProgress<ProgressNotificationValue> progress,
        string? gameFolder = null, bool appendToLatestCollection = true, bool reuseExistingModels = true, CancellationToken cancellationToken = default)
    {
        try { return Json(await ClothingOperations.PrepareUpdate(resourceRoot, packageRoot, outputParent, gameFolder, cancellationToken, new McpProgress(progress), appendToLatestCollection, reuseExistingModels)); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new McpException(error.Message, error); }
    }

    [McpServerTool(Name = "export_clothing_photos", Destructive = false, OpenWorld = false)]
    [Description("Render every texture variant of selected local resource item UUIDs as 512px WebP previews using the studio renderer. Produces the nations3 CDN hierarchy, UUID filenames and a SHA-256 manifest in a NEW outputRoot outside the resource. Masks/hair need an installed GTA V. Does not publish images or modify server manifests.")]
    public static async Task<string> ExportPhotos(string resourceRoot, Guid[] itemIds, string outputRoot,
        IProgress<ProgressNotificationValue> progress, string? gameFolder = null, CancellationToken cancellationToken = default)
    {
        try { return Json(await ClothingPhotoExporter.ExportAsync(resourceRoot, itemIds, outputRoot, gameFolder, new McpProgress(progress), cancellationToken)); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new McpException(error.Message, error); }
    }

    private sealed class McpProgress(IProgress<ProgressNotificationValue> destination) : IProgress<string>
    {
        private int sequence;
        public void Report(string message) => destination.Report(new() { Progress = Interlocked.Increment(ref sequence), Message = message });
    }
}
