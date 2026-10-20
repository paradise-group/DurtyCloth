using FooxClothStudio;

var root = Path.GetFullPath(args[0]);
var realMain = Directory.EnumerateFiles(root, "*berd_000_u.ydd").Single();
var realAlternate = Directory.EnumerateFiles(root, "*berd_000_u_1.ydd").Single();
var realFiles = ClothCatalog.WithSiblingCompanions([realMain]);
var realItems = ClothCatalog.Import(realFiles);
if (realItems.Count != 1 || realItems[0].Gender != "Feminino" ||
    realItems[0].AlternateModelPaths.Count != 1 ||
    !realItems[0].AlternateModelPaths.Contains(realAlternate) ||
    realItems[0].TexturePaths.Count == 0 ||
    realItems[0].TexturePaths.Count != realItems[0].OriginalTextureFileNames.Count)
    throw new Exception("Real YDD companion import failed.");

var synthetic = Path.GetFullPath(args[1]);
Directory.CreateDirectory(synthetic);
string Touch(string name)
{
    var path = Path.Combine(synthetic, name);
    if (!File.Exists(path)) File.WriteAllBytes(path, [1]);
    return path;
}
var main = Touch("mp_m_freemode_01_demo^berd_000_u.ydd");
var alternate = Touch("mp_m_freemode_01_demo^berd_000_u_1.ydd");
Touch("mp_m_freemode_01_demo^berd_001_u.ydd");
var wanted = Touch("mp_m_freemode_01_demo^berd_diff_000_a_uni.ytd");
var physics = Touch("mp_m_freemode_01_demo^berd_000.yld");
Touch("mp_m_freemode_01_demo^berd_001.yld");
Touch("mp_m_freemode_01_demo^berd_diff_000_a_whi.ytd");
Touch("mp_f_freemode_01_demo^berd_diff_000_b_uni.ytd");
Touch("mp_m_freemode_01_other^berd_diff_000_c_uni.ytd");
var files = ClothCatalog.WithSiblingCompanions([main]);
var items = ClothCatalog.Import(files);
if (items.Count != 1 || items[0].AlternateModelPaths.Count != 1 ||
    items[0].AlternateModelPaths[0] != alternate ||
    items[0].TexturePaths.Count != 2 || !items[0].TexturePaths.Contains(wanted) ||
    items[0].PhysicsPath != physics)
    throw new Exception("Texture, alternate or YLD matching crossed gender, collection or index.");
var restrictedModel = Touch("mp_m_freemode_01_demo^task_007_r.ydd");
var restrictedPhysics = Touch("mp_m_freemode_01_demo^task_007.yld");
if (ClothCatalog.Import([restrictedModel, restrictedPhysics]).Single().PhysicsPath != restrictedPhysics)
    throw new Exception("Restricted model did not receive its YLD.");
var project = new ClothProject { Items = [ClothCatalog.Import(files)[0]] };
project.Items[0].TexturePaths.RemoveAt(1);
project.Items[0].OriginalTextureFileNames.RemoveAt(1);
project.Items[0].AlternateModelPaths.Clear();
project.Items[0].PhysicsPath = null;
var merge = CatalogMerger.Merge(project, items, path => path, _ => { });
if (merge != (0, 1, 1, 1) || project.Items.Count != 1 ||
    project.Items[0].TexturePaths.Count != 2 ||
    project.Items[0].AlternateModelPaths.Single() != alternate ||
    project.Items[0].PhysicsPath != physics)
    throw new Exception("Reimport did not merge texture, alternate YDD and YLD.");
if (CatalogMerger.Merge(project, items, path => path, _ => { }) != (0, 0, 0, 0))
    throw new Exception("Reimport of unchanged files duplicated an asset.");
var before = project.Items[0].TexturePaths.Count;
var tooMany = new ClothItem { ModelPath = main,
    TexturePaths = Enumerable.Range(0, 27).Select(i => Path.Combine(synthetic, $"extra_{i}.ytd")).ToList() };
try
{
    CatalogMerger.Merge(project, [tooMany], path => path, _ => { });
    throw new Exception("Expected variant limit to be enforced.");
}
catch (InvalidDataException) { }
if (project.Items[0].TexturePaths.Count != before)
    throw new Exception("Rejected merge changed the project.");
Console.WriteLine($"OK real: {realItems[0].AlternateModelPaths.Count} alternate, {realItems[0].TexturePaths.Count} texture(s); synthetic: 2 YLD associations, 1 alternate, 2 matching textures");
if (args.Length > 2)
{
    var all = Directory.EnumerateFiles(args[2], "*", SearchOption.AllDirectories)
        .Where(path => new[] { ".ydd", ".ytd", ".yld" }.Contains(Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase)).ToArray();
    var imported = ClothCatalog.Import(all);
    var ytdFiles = all.Where(path => Path.GetExtension(path).Equals(".ytd",
        StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var assigned = imported.SelectMany(item => item.TexturePaths)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (!ytdFiles.SetEquals(assigned))
    {
        Console.WriteLine("Unassigned: " + string.Join(" | ", ytdFiles.Except(assigned)));
        throw new Exception($"Full resource texture coverage failed: {assigned.Count}/{ytdFiles.Count}");
    }
    Console.WriteLine($"OK full resource: {imported.Count} pieces, {assigned.Count} distinct YTD assigned, {imported.Sum(x => x.TexturePaths.Count)} total associations, {imported.Sum(x => x.AlternateModelPaths.Count)} alternates");
}
