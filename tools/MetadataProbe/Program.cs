using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var pattern = new Regex(args.Length > 1 ? args[1] : ".", RegexOptions.IgnoreCase);
foreach (var handle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(handle);
    var name = md.GetString(type.Namespace) + "." + md.GetString(type.Name);
    if (pattern.IsMatch(name)) Console.WriteLine(name);
}
