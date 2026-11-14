using System.IO;

namespace FooxClothStudio;

public static class AppearanceImporter
{
    public static List<AppearanceAsset> Import(string resourceRoot)
    {
        var result = new List<AppearanceAsset>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(resourceRoot, "stream"), "*.ytd", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains('^') || TattooImporter.LooksLikeTattooYtd(path)) continue;
            var lower = name.ToLowerInvariant();
            var kind = lower switch
            {
                _ when lower.Contains("eye_colour") => "Olhos",
                _ when lower.Contains("body_hair") => "Pelos corporais",
                _ when lower.Contains("beard") => "Barba",
                _ when lower.Contains("eyebrow") => "Sobrancelhas",
                _ when lower.Contains("blush") => "Blush",
                _ when lower.Contains("makeup") => "Maquilhagem",
                _ when lower.Contains("lips") => "Lábios",
                _ when lower.Contains("facepaint") || lower.Contains("skull") || lower.Contains("demonic") => "Pintura facial",
                _ => "Outros"
            };
            var gender = lower.Contains("eyebrowm") || lower.Contains("makeupm") || lower.Contains("lipsm") ||
                lower.StartsWith("mp_m_") ? "Male" :
                lower.Contains("eyebrowf") || lower.StartsWith("mp_f_") ? "Female" : "Both";
            result.Add(new AppearanceAsset { Name = name, Kind = kind, Gender = gender, TexturePath = path });
        }
        return result;
    }
}
