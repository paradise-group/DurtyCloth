using MessagePack;
using System.Text.Json;

var bytes = File.ReadAllBytes(args[0]);
var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
if (args.Length > 1 && args[1] == "fields")
{
    using var document = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(bytes, options));
    Console.WriteLine("Project keys: " + string.Join(", ", document.RootElement.EnumerateObject().Select(p => p.Name)));
    var sample = document.RootElement.GetProperty("ClothData").EnumerateArray().First();
    foreach (var p in sample.EnumerateObject().Where(p => p.Name != "Textures")) Console.WriteLine(p.Name + ": " + p.Value);
    Console.WriteLine("Texture: " + sample.GetProperty("Textures").EnumerateArray().FirstOrDefault());
    foreach (var entry in document.RootElement.GetProperty("ClothData").EnumerateArray())
        if (entry.TryGetProperty("PedComponentOptions", out var opts) &&
            opts.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.True))
            Console.WriteLine("Option sample: " + entry.GetProperty("DrawableType") + "/" + entry.GetProperty("Position") + " " + opts);
    return;
}
var value = MessagePackSerializer.Deserialize<object>(bytes, options);
Console.WriteLine(value?.GetType().FullName);
Console.WriteLine(value);
var json = MessagePackSerializer.ConvertToJson(bytes, options);
Console.WriteLine("Json length=" + json.Length + " first=" + json[..Math.Min(300, json.Length)]);
