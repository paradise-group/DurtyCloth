using System.IO;
using CodeWalker.GameFiles;
using FooxClothStudio;

var ydd = Path.GetFullPath(args[0]);
var ytd = Path.GetFullPath(args[1]);
var model = ModelPreview.Read(ydd);
var dictionary = new YtdFile();
dictionary.Load(File.ReadAllBytes(ytd));
var names = dictionary.TextureDict?.Textures?.data_items?
    .Select(texture => texture.Name).Where(name => name is not null).Select(name => name!)
    .ToArray() ?? [];
var result = TextureReferenceCoverage.Describe(model.Parts.Select(part => part.TextureName), names);
if (result.Length == 0) throw new Exception("Real YDD has no diffuse texture references.");
var requested = model.Parts.Select(part => part.TextureName).Where(name => name is not null)
    .Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
var complete = TextureReferenceCoverage.Describe(requested, requested);
if (complete.Length != result.Length || complete.Any(line => !line.StartsWith("✓ ")))
    throw new Exception("Matching references were not reported.");
var missing = TextureReferenceCoverage.Describe(model.Parts.Select(part => part.TextureName), []);
if (missing.Length != result.Length || missing.Any(line => !line.StartsWith("Falta: ")))
    throw new Exception("Missing references were not reported.");
Console.WriteLine($"OK: {result.Length} real YDD reference(s), YTD match state: {string.Join(", ", result)}; complete/missing controls passed");
if (TextureReferenceCoverage.Resolve("fix_broken_model", ["a", "NormalBase1", "Specular3"]) != "a") throw new Exception("Single diffuse fallback failed.");
if (TextureReferenceCoverage.Resolve("jbib_diff_000_a_uni", ["jbib_diff_123_b_uni", "other_diffuse"]) != "jbib_diff_123_b_uni") throw new Exception("Variation family failed.");
if (TextureReferenceCoverage.Resolve("unknown", ["a", "b"]) is not null) throw new Exception("Ambiguous diffuse was selected.");
if (TextureReferenceCoverage.Resolve("unknown", ["NormalBase1", "Specular3"]) is not null) throw new Exception("Normal/specular used as diffuse.");
if (TextureReferenceCoverage.Resolve("A", ["a", "b"]) != "a") throw new Exception("Exact case-insensitive selection failed.");
if (!TextureReferenceCoverage.Describe(["fix_broken_model"], ["a"]).Single().Contains("→ a")) throw new Exception("Resolved mapping not reported.");
Console.WriteLine("OK: exact, single diffuse, family, ambiguity, normal/specular exclusion and diagnostic mapping.");
