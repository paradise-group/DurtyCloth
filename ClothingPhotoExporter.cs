using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;

namespace FooxClothStudio;

public sealed record ClothingPhotoRecord(Guid ItemId, Guid TextureId, string Gender, string Kind, int Slot,
    string Collection, int Index, int TextureIndex, string File, string Sha256);
public sealed record ClothingPhotoExportResult(string OutputRoot, string ManifestPath, int Items, int Photos);

public static class ClothingPhotoExporter
{
    public static Task<ClothingPhotoExportResult> ExportAsync(string resourceRoot, Guid[] itemIds, string outputRoot,
        string? gameFolder = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        bool firstTextureOnly = true)
    {
        var completion = new TaskCompletionSource<ClothingPhotoExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Export(resourceRoot, itemIds, outputRoot, gameFolder, progress, cancellationToken, firstTextureOnly)); }
            catch (OperationCanceledException) { completion.SetCanceled(cancellationToken); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Clothing photo export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ClothingPhotoExportResult Export(string resourceRoot, Guid[] itemIds, string outputRoot,
        string? gameFolder, IProgress<string>? progress, CancellationToken cancellationToken, bool firstTextureOnly)
    {
        resourceRoot = FiveMResource.Open(resourceRoot).Root;
        outputRoot = Path.GetFullPath(outputRoot);
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot)) throw new IOException("Photo output already exists: " + outputRoot);
        var inputPrefix = Path.TrimEndingDirectorySeparator(resourceRoot) + Path.DirectorySeparatorChar;
        var outputPrefix = Path.TrimEndingDirectorySeparator(outputRoot) + Path.DirectorySeparatorChar;
        if (outputPrefix.StartsWith(inputPrefix, StringComparison.OrdinalIgnoreCase) || inputPrefix.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Photo output must be outside the resource.");
        for (var directory = new DirectoryInfo(outputRoot); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked output paths are not supported.");
        var selected = itemIds.ToHashSet();
        if (selected.Count == 0 || selected.Count != itemIds.Length) throw new ArgumentException("Select one or more distinct item UUIDs.");
        var map = ClothingIdentityMap.Read(Path.Combine(resourceRoot, ClothingIdentityMap.FileName));
        var entries = map.Entries.Where(entry => selected.Contains(entry.Id)).ToArray();
        if (entries.Length != selected.Count || entries.Any(entry => !entry.Available || entry.ModelFile is null))
            throw new InvalidDataException("Every selected UUID must identify an available model in the resource.");
        entries = entries.Select(entry => entry with { Textures = entry.Textures.Where(texture => texture.Available)
            .OrderBy(texture => texture.Index).Take(firstTextureOnly ? 1 : int.MaxValue).ToList() }).ToArray();
        if (entries.Any(entry => entry.Textures.Count == 0 || entry.Textures.Any(texture => texture.File is null)))
            throw new InvalidDataException("Photo export requires explicit available YTD files for every selected variant.");
        var files = Directory.EnumerateFiles(Path.Combine(resourceRoot, "stream"), "*", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        string Resolve(string name) => files.TryGetValue(name, out var paths) && paths.Length == 1
            ? paths[0] : throw new InvalidDataException("Missing or ambiguous stream filename: " + name);
        foreach (var entry in entries)
        {
            Resolve(entry.ModelFile!);
            foreach (var texture in entry.Textures) Resolve(texture.File!);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputRoot);
        var ownershipFile = Path.Combine(outputRoot, ".photo-build.lock");
        using (new FileStream(ownershipFile, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            var records = new List<ClothingPhotoRecord>();
            var peds = new Dictionary<string, PhotoPed>();
            var options = new PhotoImageOptions(512, "WebP", 85);
            var total = entries.Sum(entry => entry.Textures.Count);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = ModelPreview.Read(Resolve(entry.ModelFile!), false, preserveAllTriangles: true);
                var category = ClothCatalog.ResolveType(entry.ModelFile!).Category;
                var gender = entry.Model == "mp_m_freemode_01" ? "male" : "female";
                var needsPed = entry.Kind == "component" && entry.Slot is 0 or 1 or 2 || entry.Kind == "prop" && entry.Slot is 0 or 1 or 2 or 3;
                PhotoPed? ped = null;
                if (needsPed && !peds.TryGetValue(entry.Gender, out ped))
                {
                    gameFolder ??= GamePedAssets.FindGameFolder();
                    if (gameFolder is null) throw new InvalidDataException("Configure GTA V to render masks, hair and facial accessories.");
                    peds[entry.Gender] = ped = PreviewPhotoRenderer.LoadPed(gameFolder, entry.Gender);
                }
                var service = entry.Kind == "component" && entry.Slot == 2 ? $"nation_barbershop/{gender}/hair"
                    : $"nation_skinshop/clothes/{gender}/{(entry.Kind == "prop" ? "p" : "")}{entry.Slot}";
                foreach (var texture in entry.Textures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = service + $"/{entry.Id:D}.{texture.Id:D}.webp";
                    var path = Path.Combine(outputRoot, relative);
                    var images = MainWindow.ReadMatchingTextures(Resolve(texture.File!), model);
                    if (images.Count == 0) throw new InvalidDataException("No decodable textures: " + texture.File);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    PreviewPhotoRenderer.Save(path, model, images, ped, category, Color.FromRgb(0x62, 0x5f, 0x68), options);
                    using var stream = File.OpenRead(path);
                    var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    records.Add(new(entry.Id, texture.Id, gender, entry.Kind, entry.Slot, entry.Collection, entry.Index, texture.Index, relative, hash));
                    progress?.Report($"Rendered {records.Count}/{total}: {entry.Name}, texture {texture.Index}");
                }
            }
            var manifestPath = Path.Combine(outputRoot, "photo-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { formatVersion = 1, resourceRoot, items = entries.Length,
                photos = records.Count, image = options, records }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            File.Delete(ownershipFile);
            return new(outputRoot, manifestPath, entries.Length, records.Count);
        }
        catch
        {
            Directory.Delete(outputRoot, true);
            throw;
        }
    }
}
