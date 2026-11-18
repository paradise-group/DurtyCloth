using System.IO;
using System.Text.Json;
using FooxClothStudio;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static string Lua(JsonElement value) => value.ValueKind switch
{
    JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().Select(p => "[" + Quote(p.Name) + "]=" + Lua(p.Value))) + "}",
    JsonValueKind.Array => "{" + string.Join(",", value.EnumerateArray().Select(Lua)) + "}",
    JsonValueKind.String => Quote(value.GetString()!),
    JsonValueKind.Null => "nil",
    _ => value.GetRawText()
};
static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
var root = Path.GetFullPath(args.Single());
Directory.CreateDirectory(root);
var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
ClothItem Item(string name, string code, string gender = "Masculino")
{
    var model = Path.Combine(source, name + "_u.ydd"); File.WriteAllBytes(model, [1, 2, 3]);
    var textures = new[] { Path.Combine(source, name + "_diff_000_a_uni.ytd"), Path.Combine(source, name + "_diff_000_b_uni.ytd") };
    foreach (var path in textures) File.WriteAllBytes(path, [4, 5, 6]);
    return new() { Name = name, ModelPath = model, OriginalModelFileName = Path.GetFileName(model), Gender = gender,
        Category = "Item [" + code + "]", TexturePaths = textures.ToList(), OriginalTextureFileNames = textures.Select(Path.GetFileName).ToList()! };
}
var first = Item("First", "jbib"); var second = Item("Second", "jbib");
var watch = Item("Watch", "p_lwrist"); var female = Item("Female", "lowr", "Feminino");
var project = new ClothProject { Name = "idprobe", DlcName = "idprobe", Items = [first, second, watch, female] };
second.TexturePaths.Insert(1, ""); second.OriginalTextureFileNames.Insert(1, "(dummy)");
var reserved = new ClothItem { IsDummy = true, Name = "Reserved", Gender = "Masculino", Category = "Item [jbib]", ExportPosition = 3 };
project.Items.Add(reserved);
first.ExportPosition = 0; second.ExportPosition = 1;
CollectionIndices.Publish(project);
project.UsePermanentClothingIds = true;
foreach (var item in project.Items) ClothingIdentityMap.EnsureTextures(item);
var legacy = new ClothProject { Items = [new ClothItem { Id = Guid.Empty, TexturePaths = ["legacy.ytd"], TextureIds = [Guid.Empty] }] };
Check(ClothingIdentityMap.GenerateMissing(legacy) == 2, "Legacy bulk generation did not fill missing IDs");
var legacyPieceId = legacy.Items[0].Id; var legacyTextureId = legacy.Items[0].TextureIds[0];
Check(ClothingIdentityMap.GenerateMissing(legacy) == 0 && legacy.Items[0].Id == legacyPieceId && legacy.Items[0].TextureIds[0] == legacyTextureId, "Bulk generation changed existing IDs");
var itemId = first.Id; var textureId = first.TextureIds[0];
var firstMap = ClothingIdentityMap.Describe(project);
Check(firstMap.Entries.Single(e => e.Id == watch.Id).Slot == 6, "Wrong wrist anchor");
Check(firstMap.Entries.Single(e => e.Id == female.Id).Model == "mp_f_freemode_01", "Wrong female ped");
void Fixture(string name, ClothingIdentityDocument map)
{
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(map, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    File.WriteAllText(Path.Combine(root, name + ".lua"), "return " + Lua(json.RootElement));
}
Fixture("before", firstMap);
first.Name = "Renamed"; first.ExportPosition = 2; second.ExportPosition = 0;
first.TexturePaths.Reverse(); first.OriginalTextureFileNames.Reverse(); first.TextureIds.Reverse();
var afterMap = ClothingIdentityMap.Describe(project);
var updated = afterMap.Entries.Single(e => e.Id == itemId);
Check(updated.Index == 1 && updated.Textures.Single(t => t.Id == textureId).Index == 1, "Reorder lost stable IDs");
Check(CollectionIndices.Validate(project).Count == 0, "Permanent ID mode blocks reordering");
project.UsePermanentClothingIds = false;
Check(CollectionIndices.Validate(project).Count > 0, "Legacy protection was lost");
project.UsePermanentClothingIds = true;
Fixture("after", afterMap);
var redirectTarget = afterMap.Entries.Single(entry => entry.Id == second.Id);
var redirectedMap = afterMap with { Entries = afterMap.Entries.Select(entry => entry.Id != itemId ? entry : entry with
{
    Textures = entry.Textures.Select(texture => texture.Id != textureId ? texture : texture with
        { Available = false, RedirectItemId = redirectTarget.Id, RedirectTextureId = redirectTarget.Textures[0].Id }).ToList()
}).ToList() };
ClothingIdentityMap.Validate(redirectedMap);
Fixture("redirected", redirectedMap);
var wrongRedirect = redirectedMap with { Entries = redirectedMap.Entries.Select(entry => entry.Id != redirectTarget.Id ? entry : entry with { Slot = 4 }).ToList() };
try { ClothingIdentityMap.Validate(wrongRedirect); throw new Exception("Cross-slot redirect accepted"); } catch (InvalidDataException) { }
var redirectJson = JsonSerializer.Serialize(redirectedMap);
Check(JsonSerializer.Serialize(JsonSerializer.Deserialize<ClothingIdentityDocument>(redirectJson)) == redirectJson, "Texture redirects lost on JSON round-trip");
var snapshot = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
Check(snapshot.Items[0].Id == itemId && snapshot.Items[0].TextureIds[1] == textureId, "JSON lost identity");
var bundle = Path.Combine(root, "project.fcsbundle");
ProjectBundle.Export(project, Path.GetFullPath, bundle);
var restoredPath = ProjectBundle.Import(bundle, root);
var restored = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(restoredPath))!;
Check(restored.Items[0].Id == itemId && restored.Items[0].TextureIds[1] == textureId, "Bundle lost identity");
var output = AddonExporter.Export(project, Path.GetFullPath, root);
Check(FiveMResourceValidator.Validate(output).Count == 0, "Generated addon metadata is invalid");
foreach (var entry in afterMap.Entries.Where(entry => entry.Available))
{
    Check(Directory.GetFiles(Path.Combine(output, "stream"), entry.ModelFile!, SearchOption.AllDirectories).Length == 1, "Map model differs from exported asset");
    foreach (var texture in entry.Textures.Where(t => t.Available))
        Check(Directory.GetFiles(Path.Combine(output, "stream"), texture.File!, SearchOption.AllDirectories).Length == 1, "Map texture differs from exported asset");
}
Check(File.ReadAllText(Path.Combine(output, "fxmanifest.lua")).Contains("client_script 'clothing-ids.client.lua'"), "Adapter not registered");
var imported = ClothCatalog.Import(Directory.EnumerateFiles(Path.Combine(output, "stream"), "*", SearchOption.AllDirectories));
ClothingIdentityMap.Restore(output, imported);
var redirectFirst = Item("RedirectFirst", "jbib");
var redirectSecond = Item("RedirectSecond", "jbib");
var redirectSource = AddonExporter.Export(new ClothProject { Name = "redirect_fixture", DlcName = "redirect_fixture",
    UsePermanentClothingIds = true, Items = [redirectFirst, redirectSecond] }, Path.GetFullPath, Path.Combine(root, "redirect-fixture"));
var redirectSourceMap = ClothingIdentityMap.Read(Path.Combine(redirectSource, ClothingIdentityMap.FileName));
var retiredFile = redirectSourceMap.Entries.Single(entry => entry.Id == redirectFirst.Id).Textures[0].File!;
File.Delete(Directory.GetFiles(redirectSource, retiredFile, SearchOption.AllDirectories).Single());
var placeholderMap = redirectSourceMap with { Entries = redirectSourceMap.Entries.Select(entry => entry with
    { Textures = entry.Textures.Select(texture => texture.Id == redirectFirst.TextureIds[0]
        ? texture with { File = null, Available = false, RedirectItemId = redirectSecond.Id, RedirectTextureId = redirectSecond.TextureIds[0] } : texture).ToList() }).ToList() };
ClothingIdentityMap.Write(placeholderMap, redirectSource);
var redirectItems = ClothCatalog.Import(Directory.GetFiles(Path.Combine(redirectSource, "stream"), "*", SearchOption.AllDirectories));
ClothingIdentityMap.Restore(redirectSource, redirectItems);
var redirectProject = new ClothProject { SourceResourceRoot = redirectSource, Items = redirectItems };
var reimportedRedirects = OriginalResourceExporter.Validate(redirectProject, Path.GetFullPath, redirectSource);
Check(reimportedRedirects.Entries.Single(entry => entry.Id == redirectFirst.Id).Textures[0].RedirectItemId == redirectSecond.Id,
    "Inactive redirected texture slots must survive source reimport and subsequent resource updates.");
Check(imported.Single(i => i.Id == itemId).TextureIds[1] == textureId, "Reimport lost identity");
Check(imported.Single(i => i.Id == second.Id).TexturePaths[1] == "" && imported.Single(i => i.Id == reserved.Id).IsDummy, "Reimport lost reserved drawable/texture slots");
Check(ClothingIdentityMap.SourceId("x.ydd", "Masculino") == ClothingIdentityMap.SourceId("X.YDD", "Masculino"), "Source identity changes across imports");
var copied = ClothEditActions.CopyForOtherGender(project, first);
Check(copied.Id != itemId && !copied.TextureIds.Intersect(first.TextureIds).Any(), "Copy reused identity");
project.Items.Remove(copied);
first.TexturePaths.RemoveAt(1); first.OriginalTextureFileNames.RemoveAt(1); first.TextureIds.RemoveAt(1);
Fixture("removed-texture", ClothingIdentityMap.Describe(project));
ClothEditActions.ReplaceWithDummy(first);
Fixture("removed-item", ClothingIdentityMap.Describe(project));
File.WriteAllText(Path.Combine(root, "ids.lua"), "return { itemId=" + Quote(itemId.ToString()) + ", textureId=" + Quote(textureId.ToString()) + ", watchId=" + Quote(watch.Id.ToString()) + ", watchTextureId=" + Quote(watch.TextureIds[0].ToString()) + " }");
var duplicate = new ClothingIdentityDocument(1, [updated, updated]);
try { ClothingIdentityMap.Validate(duplicate); throw new Exception("Duplicate IDs accepted"); } catch (InvalidDataException) { }
Console.WriteLine("PASS: stable IDs across reorder, rename, JSON, bundle, reimport; real addon metadata and map assets; gender/prop mapping; legacy guards, copies, missing variants and duplicate IDs.");
Console.WriteLine(output);

// Existing packs must keep their original collections, source indices and assets.
var original = Path.Combine(root, "original_fixture");
var originalStream = Path.Combine(original, "stream"); Directory.CreateDirectory(originalStream);
File.WriteAllText(Path.Combine(original, "fxmanifest.lua"), "fx_version 'cerulean'\ngame 'gta5'\nfiles { 'stream/**/*' }\n");
File.WriteAllBytes(Path.Combine(originalStream, "original.ymt"), [10, 11, 12]);
File.WriteAllBytes(Path.Combine(originalStream, "old_tattoo.ytd"), [13, 14, 15]);
ClothItem SourceItem(string collection, int index, string suffix, bool withTexture)
{
    var stem = "mp_m_freemode_01_" + collection + "^jbib_" + index.ToString("D3");
    var file = stem + "_" + suffix + ".ydd"; var path = Path.Combine(originalStream, file); File.WriteAllBytes(path, [1, 2, 3]);
    var item = new ClothItem { Name = file, OriginalModelFileName = file, ModelPath = path,
        Gender = "Masculino", Category = "Item [jbib]", SourceIndex = index, ExportPosition = 99 - index };
    if (withTexture)
        foreach (var skin in new[] { "uni", "whi" })
        {
            var texture = "mp_m_freemode_01_" + collection + "^jbib_diff_" + index.ToString("D3") + "_a_" + skin + ".ytd";
            var texturePath = Path.Combine(originalStream, texture); File.WriteAllBytes(texturePath, [4, 5, 6]);
            item.TexturePaths.Add(texturePath); item.OriginalTextureFileNames.Add(texture);
        }
    return item;
}
var sourceFirst = SourceItem("source_one", 4, "u", true);
var sourcePhysics = Path.ChangeExtension(sourceFirst.ModelPath, ".yld"); File.WriteAllBytes(sourcePhysics, [21, 22, 23]);
var savedPhysics = Path.Combine(root, "physics_" + Path.GetFileName(sourcePhysics)); File.Copy(sourcePhysics, savedPhysics);
sourceFirst.PhysicsPath = savedPhysics;
var sourceAlias = SourceItem("source_one", 4, "r", true);
var inherited = SourceItem("source_two", 17, "u", false);
var sourceProject = new ClothProject { Name = "original_fixture", Items = [sourceFirst, sourceAlias, inherited] };
var preserved = OriginalResourceExporter.Export(sourceProject, Path.GetFullPath, original, Path.Combine(root, "preserved"));
foreach (var file in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith("fxmanifest.lua")))
    Check(AssetContent.Equivalent(file, Path.Combine(preserved, Path.GetRelativePath(original, file))), "Original asset/metadata changed");
var sourceMap = OriginalResourceExporter.Describe(sourceProject);
Fixture("original-source", sourceMap);
File.WriteAllText(Path.Combine(root, "ids.lua"), "return { itemId=" + Quote(itemId.ToString()) + ", textureId=" + Quote(textureId.ToString()) +
    ", watchId=" + Quote(watch.Id.ToString()) + ", watchTextureId=" + Quote(watch.TextureIds[0].ToString()) +
    ", sourceItemId=" + Quote(sourceFirst.Id.ToString()) + ", sourceTextureId=" + Quote(sourceFirst.TextureIds[0].ToString()) +
    ", sourceAliasId=" + Quote(sourceAlias.Id.ToString()) + ", sourceAliasTextureId=" + Quote(sourceAlias.TextureIds[1].ToString()) +
    ", inheritedId=" + Quote(inherited.Id.ToString()) + ", inheritedTexture3=" + Quote(ClothingIdentityMap.InheritedTextureId(inherited.Id, 3).ToString()) +
    ", inheritedTexture4=" + Quote(ClothingIdentityMap.InheritedTextureId(inherited.Id, 4).ToString()) + " }");
Check(sourceMap.Entries.Single(e => e.Id == sourceFirst.Id).Index == 4 && sourceMap.Entries.Single(e => e.Id == inherited.Id).Collection == "source_two" &&
    sourceMap.Entries.Single(e => e.Id == inherited.Id).Index == 17, "Original native addresses were regrouped/renumbered");
Check(sourceMap.Entries.Single(e => e.Id == sourceAlias.Id).AliasOf == sourceFirst.Id, "Model representations lost explicit native alias");
Check(sourceMap.Entries.Single(e => e.Id == sourceFirst.Id).Textures[1].AliasOf == sourceFirst.TextureIds[0], "Skin texture aliases lost native index");
Check(sourceMap.Entries.Single(e => e.Id == inherited.Id).Textures.Count == 256 && sourceMap.Entries.Single(e => e.Id == inherited.Id).Textures.All(texture => texture.Inherited), "Inherited native texture slots missing from ID map");
var sourceImported = ClothCatalog.Import(Directory.EnumerateFiles(Path.Combine(preserved, "stream")));
ClothingIdentityMap.Restore(preserved, sourceImported);
Check(sourceImported.Single(i => i.Id == inherited.Id).TexturePaths.Count == 0, "Inherited texture treated as a missing external file");
var changedIdentity = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(sourceProject))!;
changedIdentity.Items[0].Id = Guid.NewGuid();
try { OriginalResourceExporter.Validate(changedIdentity, Path.GetFullPath, preserved); throw new Exception("Existing published item ID was overwritten"); }
catch (InvalidDataException) { }
changedIdentity = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(sourceProject))!;
changedIdentity.Items[0].TextureIds[0] = Guid.NewGuid();
try { OriginalResourceExporter.Validate(changedIdentity, Path.GetFullPath, preserved); throw new Exception("Existing published texture ID was overwritten"); }
catch (InvalidDataException) { }
OriginalResourceExporter.Validate(sourceProject, Path.GetFullPath, preserved);
File.WriteAllText(Path.Combine(original, "clothing-ids.client.lua"), "-- unrelated custom script");
try { OriginalResourceExporter.Validate(sourceProject, Path.GetFullPath, original); throw new Exception("Reserved custom script would have been overwritten"); }
catch (InvalidDataException) { }
File.Delete(Path.Combine(original, "clothing-ids.client.lua"));
var modified = Path.Combine(root, "modified.ydd"); File.WriteAllBytes(modified, [99]); sourceFirst.ModelPath = modified;
try { OriginalResourceExporter.Export(sourceProject, Path.GetFullPath, original, Path.Combine(root, "rejected")); throw new Exception("Asset edits silently omitted"); }
catch (InvalidDataException) { }
Check(!Directory.Exists(Path.Combine(root, "rejected", "original_fixture")), "Rejected source build left output");
Console.WriteLine("PASS: original resource copy preserves asset/metadata bytes, collections and indices; explicit source representation aliases, inherited textures and reimport; asset edits rejected before copying.");
