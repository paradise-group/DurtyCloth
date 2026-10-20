using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;

namespace FooxClothStudio;

public static class ClothingDrop
{
    public static bool Accepts(System.Windows.IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("FileGroupDescriptorW") || data.GetDataPresent("FileGroupDescriptor");
    private static bool Supported(string path) => new[] { ".ydd", ".ytd", ".yld" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static string[] Materialize(System.Windows.IDataObject data)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio", "drop-imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = new List<string>();
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var files = paths.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : new[] { path }).Where(Supported).ToArray();
                // Dragging a model imports only what the source actually supplied; textures are dropped separately.
                files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files.Where(Supported))
                {
                    var parent = Path.GetDirectoryName(Path.GetFullPath(file))!;
                    if (!folders.TryGetValue(parent, out var folder)) { var gender = ClothCatalog.ResolveGender(parent + Path.DirectorySeparatorChar); folder = Path.Combine(root, gender == "Feminino" ? "female" : gender == "Masculino" ? "male" : "unknown", folders.Count + "_" + Path.GetFileName(parent)); folders[parent] = folder; Directory.CreateDirectory(folder); }
                    var target = Path.Combine(folder, Path.GetFileName(file)); File.Copy(file, target); result.Add(target);
                }
            }
            else
            {
                var unicode = data.GetDataPresent("FileGroupDescriptorW");
                var descriptor = data.GetData(unicode ? "FileGroupDescriptorW" : "FileGroupDescriptor");
                using var ownedStream = descriptor is byte[] bytes ? new MemoryStream(bytes) : null;
                var stream = descriptor as Stream ?? ownedStream ?? throw new InvalidDataException("Unsupported file descriptor.");
                using var buffer = new MemoryStream(); CopyFromStart(stream, buffer); var block = buffer.ToArray();
                if (block.Length < 4) throw new InvalidDataException("Invalid file descriptor.");
                var count = BitConverter.ToInt32(block, 0); var size = unicode ? 592 : 332;
                if (count < 1 || count > 4096 || block.Length < 4L + (long)count * size) throw new InvalidDataException("Invalid file count.");
                for (var index = 0; index < count; index++)
                {
                    var raw = (unicode ? Encoding.Unicode : Encoding.Default).GetString(block, 4 + index * size + 72, unicode ? 520 : 260).Split('\0')[0];
                    if (!Supported(raw)) continue;
                    if (Path.IsPathRooted(raw) || raw.Replace('/', '\\').Split('\\').Any(p => p is ".." or "." || p.Contains(':'))) throw new InvalidDataException("Invalid dropped filename.");
                    var target = Path.GetFullPath(Path.Combine(root, raw));
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid dropped path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var output = new FileStream(target, FileMode.CreateNew);
                    ReadVirtualFile(data, index, count, output);
                    var offset = 4 + index * size;
                    if ((BitConverter.ToUInt32(block, offset) & 0x40) != 0)
                    {
                        var declaredSize = ((ulong)BitConverter.ToUInt32(block, offset + 64) << 32) | BitConverter.ToUInt32(block, offset + 68);
                        if (declaredSize > long.MaxValue || output.Length < (long)declaredSize) throw new InvalidDataException("Incomplete dropped file: " + raw);
                        output.SetLength((long)declaredSize);
                    }
                    result.Add(target);
                }
            }
            if (result.Count == 0) Directory.Delete(root, true);
            return result.ToArray();
        }
        catch { Directory.Delete(root, true); throw; }
    }
    private static void ReadVirtualFile(System.Windows.IDataObject data, int index, int count, Stream output)
    {
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject native)
        {
            if (count == 1 && data.GetData("FileContents") is Stream stream) { CopyFromStart(stream, output); return; }
            throw new InvalidDataException("The source did not expose indexed file contents. Extract the files from OpenIV and drag them again.");
        }
        var format = new FORMATETC { cfFormat = (short)DataFormats.GetDataFormat("FileContents").Id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = index, tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL };
        native.GetData(ref format, out var medium);
        try
        {
            if (medium.tymed == TYMED.TYMED_ISTREAM)
            {
                var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                var countPointer = Marshal.AllocCoTaskMem(4);
                try { var bytes = new byte[65536]; while (true) { stream.Read(bytes, bytes.Length, countPointer); var read = Marshal.ReadInt32(countPointer); if (read == 0) break; output.Write(bytes, 0, read); } }
                finally { Marshal.FreeCoTaskMem(countPointer); Marshal.ReleaseComObject(stream); }
            }
            else if (medium.tymed == TYMED.TYMED_HGLOBAL)
            {
                CopyGlobalMemory(medium.unionmember, output);
            }
            else throw new InvalidDataException("Unsupported dropped file content.");
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    private static void CopyGlobalMemory(IntPtr memory, Stream output)
    {
        var length = checked((long)GlobalSize(memory).ToUInt64());
        if (length == 0) return;
        var pointer = GlobalLock(memory);
        if (pointer == IntPtr.Zero) throw new IOException("Cannot read dropped file.");
        try
        {
            var bytes = new byte[65536];
            for (long offset = 0; offset < length; offset += bytes.Length)
            {
                var count = (int)Math.Min(bytes.Length, length - offset);
                Marshal.Copy(new IntPtr(checked(pointer.ToInt64() + offset)), bytes, 0, count);
                output.Write(bytes, 0, count);
            }
        }
        finally { GlobalUnlock(memory); }
    }
    private static void CopyFromStart(Stream source, Stream output)
    {
        var position = source.CanSeek ? source.Position : (long?)null;
        try { if (position.HasValue) source.Position = 0; source.CopyTo(output); }
        finally { if (position.HasValue) source.Position = position.Value; }
    }
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
}
