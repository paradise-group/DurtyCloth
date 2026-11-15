using System.Reflection;
using System.Text.Json;
using FooxClothStudio;

internal static class PhotoIndexTests
{
    public static void Run()
    {
        // Exercise the actual anonymous index entry types emitted by the app,
        // including the success, per-texture error and per-appearance error shapes.
        var entries = typeof(MainWindow).Assembly.GetTypes()
            .Where(type => type.Name.StartsWith("<>f__AnonymousType") && type.IsGenericTypeDefinition)
            .Where(type => type.GetProperty("kind") is not null &&
                (type.GetProperty("Kind") is not null || type.GetProperty("appearanceKind") is not null))
            .ToArray();
        if (entries.Length != 3) throw new Exception($"Expected 3 appearance index entry shapes, found {entries.Length}.");

        foreach (var entry in entries)
        {
            var concrete = entry.MakeGenericType(Enumerable.Repeat(typeof(string), entry.GetGenericArguments().Length).ToArray());
            var constructor = concrete.GetConstructors().Single();
            var values = constructor.GetParameters().Select(parameter => (object)(parameter.Name switch
            {
                "kind" => "appearance",
                "appearanceKind" or "Kind" => "Olhos",
                "status" => "error",
                _ => "fixture"
            })).ToArray();
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(constructor.Invoke(values), concrete));
            if (json.RootElement.GetProperty("kind").GetString() != "appearance" ||
                json.RootElement.GetProperty("appearanceKind").GetString() != "Olhos")
                throw new Exception("Appearance index lost its entry type or appearance category.");
        }
        Console.WriteLine("PASS: all 3 appearance photo index entry shapes serialize with separate entry type and appearance category.");
    }
}
