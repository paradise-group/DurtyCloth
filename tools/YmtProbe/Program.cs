using CodeWalker.GameFiles;
using System.Xml;

if (args[0] == "fix-prop-textures")
{
    var target = Path.GetFullPath(args[1]);
    var allowed = Path.GetFullPath(@"D:\FooxClothStudio\validation") + Path.DirectorySeparatorChar;
    if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Ajuste permitido apenas nos arquivos de validação do Foox Cloth Studio.");
    var original = new YmtFile();
    original.Load(File.ReadAllBytes(target));
    var xmlDocument = new XmlDocument();
    xmlDocument.LoadXml(MetaXml.GetXml(original.Meta));
    foreach (XmlNode? prop in xmlDocument.SelectNodes("/CPedVariationInfo/propInfo/aPropMetaData/Item")!)
    {
        var textureIndex = 0;
        foreach (XmlNode? texture in prop!.SelectNodes("texData/Item")!)
            texture!.SelectSingleNode("texId")!.Attributes!["value"]!.Value = (textureIndex++).ToString();
    }
    var bytes = XmlMeta.GetData(xmlDocument, MetaFormat.RSC, Path.GetDirectoryName(target)!);
    var check = new YmtFile();
    check.Load(bytes);
    if (check.Meta is null) throw new InvalidDataException("YMT recompilado inválido.");
    File.WriteAllBytes(target, bytes);
    Console.WriteLine($"Updated {target} ({bytes.Length} bytes)");
    return;
}

var path = args[0];
var ymt = new YmtFile();
ymt.Load(File.ReadAllBytes(path));
Console.WriteLine($"Format={ymt.FileFormat} Type={ymt.ContentType} Meta={ymt.Meta is not null} Pso={ymt.Pso is not null}");
if (ymt.Meta is not null)
{
    var xml = MetaXml.GetXml(ymt.Meta);
    Console.WriteLine($"XML chars={xml.Length}");
    File.WriteAllText(args[1], xml);
    Console.WriteLine("Formats=" + string.Join(",", Enum.GetNames<MetaFormat>()));
    var doc = new XmlDocument();
    doc.LoadXml(xml);
    var rebuilt = XmlMeta.GetData(doc, MetaFormat.RSC, Path.GetDirectoryName(args[1])!);
    Console.WriteLine($"Rebuilt bytes={rebuilt?.Length}");
    if (rebuilt is not null) File.WriteAllBytes(args[1] + ".ymt", rebuilt);
}
