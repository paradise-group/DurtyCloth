namespace FooxClothStudio;

public static class TextureReferenceCoverage
{
    public static string? Resolve(string? requestedName, IEnumerable<string> availableNames)
    {
        var available = availableNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var exact = available.FirstOrDefault(name => string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var diffuse = available.Where(name => !System.Text.RegularExpressions.Regex.IsMatch(name,
            "(normal|specular|bump|_n$|_s$|_normal|_spec)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray();
        static string Family(string name) => System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"(?<=_diff_)\d+_[a-z](?=_)", "variation");
        var family = requestedName is null ? [] : diffuse.Where(name => Family(name) == Family(requestedName)).ToArray();
        return family.Length == 1 ? family[0] : diffuse.Length == 1 ? diffuse[0] : null;
    }
    public static string[] Describe(IEnumerable<string?> requestedNames,
        IEnumerable<string> availableNames)
    {
        var requested = requestedNames.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var available = availableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requested.Select(name => Resolve(name, available) is string selected
            ? "✓ " + name + (string.Equals(name, selected, StringComparison.OrdinalIgnoreCase) ? "" : " → " + selected)
            : "Falta: " + name).ToArray();
    }
}
