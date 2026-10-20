using System.IO;
using FooxClothStudio;
var hats = Directory.EnumerateFiles("validation", "*p_head_000.ydd", SearchOption.AllDirectories).Take(3).ToArray();
foreach (var gender in new[] { "mp_m_freemode_01", "mp_f_freemode_01" }) {
var head = Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ParadiseClothStudio","ped-cache-v3"), "head_000_r.ydd", SearchOption.AllDirectories).First(p=>p.Contains(gender));
var matrix=ModelPreview.HeadAttachment(head);
foreach(var hat in hats) {
var source=ModelPreview.Read(hat,false); var before=source.Parts.SelectMany(p=>p.Positions).ToArray();
var placed=ModelPreview.AttachToHead(source,matrix); var after=placed.Parts.SelectMany(p=>p.Positions).ToArray();
var headPoints=ModelPreview.Read(head,false).Parts.SelectMany(p=>p.Positions).ToArray();
if(!before.SequenceEqual(source.Parts.SelectMany(p=>p.Positions))) throw new Exception("Source mutated");
if(after.Max(p=>p.Y)<headPoints.Min(p=>p.Y) || after.Min(p=>p.Y)>headPoints.Max(p=>p.Y)) throw new Exception("Hat misses head height");
if(Math.Abs((after[0]-after[1]).Length-(before[0]-before[1]).Length)>0.00001) throw new Exception("Hat distorted");
Console.WriteLine($"OK {gender}: hat height {after.Min(p=>p.Y):F3}..{after.Max(p=>p.Y):F3}, head {headPoints.Min(p=>p.Y):F3}..{headPoints.Max(p=>p.Y):F3}; source preserved.");
}}
