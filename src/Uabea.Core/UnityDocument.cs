using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;

namespace Uabea.Core;

/// <summary>One worker owns a document. Never call concurrently: engine readers are seekable/shared.</summary>
public sealed class UnityDocument : IDisposable
{
    readonly AssetsManager manager = new() { UseTemplateFieldCache = false, UseMonoTemplateFieldCache = false };
    readonly Dictionary<string, AssetsFileInstance> files = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> resources = new(StringComparer.Ordinal);
    readonly Dictionary<string, Change> changes = new(StringComparer.Ordinal);
    readonly List<Stream> owned = [];
    readonly string temp;
    readonly string? classData;
    BundleFileInstance? bundle;
    bool disposed;
    public string SourcePath { get; }
    public DocumentLimits Limits { get; }
    public bool IsBundle => bundle != null;
    public IReadOnlyList<string> Logs => logs;
    readonly List<string> logs = [];
    public IReadOnlyList<Change> Changes => changes.Values.ToArray();
    public BundleCompression OriginalCompression { get; private set; }

    UnityDocument(string path, string workspace, string? classData, DocumentLimits limits)
    {
        SourcePath = Path.GetFullPath(path);
        this.classData = classData;
        Limits = limits;
        temp = Path.Combine(workspace, "session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
    }

    public static UnityDocument Open(string path, string workspace, string? classData = null,
        CancellationToken cancel = default, DocumentLimits? limits = null)
    {
        var doc = new UnityDocument(path, workspace, classData, limits ?? new());
        try { doc.Load(cancel); return doc; }
        catch (OperationCanceledException) { doc.Dispose(); throw; }
        catch (Exception e)
        {
            doc.Dispose();
            if (e is UnityFormatException) throw;
            throw new UnityFormatException("No se pudo abrir el archivo Unity. Está dañado, cifrado o usa una variante no compatible. " + e.Message, e);
        }
    }

    void Load(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        long size = new FileInfo(SourcePath).Length;
        if (size < 20 || size > Limits.MaxFileBytes) throw new UnityFormatException("Archivo vacío, truncado o mayor que el límite de 2 GiB.");
        if (classData != null)
        {
            using var db = File.OpenRead(classData);
            manager.LoadClassPackage(db);
        }
        using (var input = File.OpenRead(SourcePath))
        {
            var magic = new byte[8]; input.ReadExactly(magic);
            if (Encoding.ASCII.GetString(magic) == "UnityFS\0")
            {
                input.Position = 0;
                Preflight.Bundle(input, Limits);
                var raw = new AssetBundleFile();
                raw.Read(new AssetsFileReader(File.OpenRead(SourcePath)));
                try
                {
                    Preflight.BundleRanges(raw, Limits);
                    OriginalCompression = raw.GetCompressionType() switch
                    {
                        AssetBundleCompressionType.LZMA => BundleCompression.Lzma,
                        AssetBundleCompressionType.LZ4 => BundleCompression.Lz4,
                        _ => BundleCompression.None
                    };
                    if (raw.DataIsCompressed)
                    {
                        string unpacked = Path.Combine(temp, "unpacked.bundle");
                        using (var w = new AssetsFileWriter(File.Create(unpacked))) raw.Unpack(w);
                        cancel.ThrowIfCancellationRequested();
                        bundle = manager.LoadBundleFile(unpacked, false);
                        logs.Add("LZMA descomprimido a disco privado.");
                    }
                    else bundle = manager.LoadBundleFile(SourcePath, false);
                }
                finally { raw.Close(); }
                for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var entry = bundle.file.BlockAndDirInfo.DirectoryInfos[i];
                    if (!entry.IsSerialized && !bundle.file.IsAssetsFile(i)) continue;
                    using (var s = EntryStream(entry)) Preflight.Assets(s, Limits);
                    var file = manager.LoadAssetsFileFromBundle(bundle, i, false)
                        ?? throw new UnityFormatException("Entrada serializada inválida: " + entry.Name);
                    CheckAssets(file);
                    files.Add(entry.Name, file);
                }
            }
            else
            {
                if (Encoding.ASCII.GetString(magic).StartsWith("Unity", StringComparison.Ordinal))
                    throw new UnityFormatException("Solo UnityFS es compatible como bundle. UnityWeb/UnityRaw/UnityArchive no están implementados.");
                input.Position = 0;
                Preflight.Assets(input, Limits);
                var file = manager.LoadAssetsFile(SourcePath, false);
                CheckAssets(file);
                files.Add("__main__", file);
            }
        }
        logs.Add($"Lectura completa: {files.Count} archivos serializados, {files.Values.Sum(f => f.file.Metadata.AssetInfos.Count)} objetos.");
    }

    void CheckAssets(AssetsFileInstance inst)
    {
        var f = inst.file;
        if (f.Metadata.AssetInfos.Count > Limits.MaxObjects) throw new UnityFormatException("Demasiados objetos.");
        HashSet<long> ids = [];
        foreach (var info in f.Metadata.AssetInfos)
        {
            long offset = info.GetAbsoluteByteOffset(f);
            if (!ids.Add(info.PathId) || offset < f.Header.DataOffset || offset > f.Header.FileSize || info.ByteSize > f.Header.FileSize - offset)
                throw new UnityFormatException("Tabla de objetos dañada o PathID duplicado.");
        }
    }

    AssetTypeTemplateField Template(AssetsFileInstance inst, AssetFileInfo info)
    {
        if (manager.ClassPackage != null) manager.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);
        // A MonoBehaviour without its own type tree cannot safely be edited as a truncated base class.
        if (info.TypeId == 114 && !inst.file.Metadata.TypeTreeEnabled)
            throw new UnityFormatException("MonoBehaviour sin TypeTree: requiere el esquema del script. RAW disponible.");
        return manager.GetTemplateBaseField(inst, info)
            ?? throw new UnityFormatException("No se encontró TypeTree ni base de clases compatible.");
    }

    (AssetsFileInstance file, AssetFileInfo info) Resolve(UnityObject obj)
    {
        var file = files[obj.File];
        return (file, file.file.GetAssetInfo(obj.PathId) ?? throw new KeyNotFoundException("PathID no encontrado."));
    }

    public IReadOnlyList<BundleEntry> Entries => bundle == null ? [] : bundle.file.BlockAndDirInfo.DirectoryInfos
        .Select(e => new BundleEntry(e.Name, e.Replacer?.GetSize() ?? e.DecompressedSize,
            files.ContainsKey(e.Name), e.Replacer != null || changes.Values.Any(c => c.Key.StartsWith(e.Name + ":", StringComparison.Ordinal)))).ToArray();

    public IReadOnlyList<UnityObject> ListObjects(CancellationToken cancel = default)
    {
        var output = new List<UnityObject>();
        foreach (var (key, inst) in files)
        {
            var containers = ContainerMap.Read(inst, info => ReadField(new(key, info.PathId, info.TypeId, "", "", info.ByteSize, "", "", false)), logs);
            foreach (var info in inst.file.Metadata.AssetInfos)
            {
                cancel.ThrowIfCancellationRequested();
                string type = Enum.GetName(typeof(AssetClassID), info.TypeId) ?? $"Class{info.TypeId}";
                string name = type;
                var row = new UnityObject(key, info.PathId, info.TypeId, type, name,
                    info.Replacer?.GetSize() ?? info.ByteSize, containers.GetValueOrDefault(info.PathId, ""),
                    inst.file.Metadata.UnityVersion, info.Replacer != null);
                try
                {
                    var template = Template(inst, info);
                    if (template.Children.Count > 0 && template.Children[0].Name == "m_Name")
                    {
                        using var raw = OpenRaw(row);
                        var reader = new AssetsFileReader(raw) { BigEndian = inst.file.Header.Endianness };
                        int count = reader.ReadInt32();
                        if (count >= 0 && count <= 8192 && count <= raw.Length - 4) name = Encoding.UTF8.GetString(reader.ReadBytes(count));
                    }
                    else if (row.Size <= 256 * 1024)
                    {
                        var field = ReadField(row)["m_Name"];
                        if (!field.IsDummy) name = field.AsString;
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException) { /* RAW remains usable; inspection reports exact errors. */ }
                output.Add(row with { Name = name });
            }
        }
        return output;
    }

    public Stream OpenRaw(UnityObject obj, bool original = false)
    {
        var (inst, info) = Resolve(obj);
        if (!original && info.Replacer != null)
        {
            var s = info.Replacer.GetPreviewStream(); return new SegmentStream(s, 0, s.Length);
        }
        return new SegmentStream(inst.file.Reader.BaseStream, info.GetAbsoluteByteOffset(inst.file), info.ByteSize);
    }

    public byte[] ReadRaw(UnityObject obj, bool original = false)
    {
        using var stream = OpenRaw(obj, original);
        if (stream.Length > Limits.MaxObjectBytes) throw new UnityFormatException("Objeto mayor de 64 MiB: usa exportación RAW por streaming.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }

    public AssetTypeValueField ReadField(UnityObject obj)
    {
        var (inst, info) = Resolve(obj);
        var data = ReadRaw(obj);
        var template = Template(inst, info);
        using var stream = new MemoryStream(data, false);
        var reader = new AssetsFileReader(stream) { BigEndian = inst.file.Header.Endianness };
        return template.MakeValue(reader, manager.GetRefTypeManager(inst));
    }

    public AssetTypeValueField ReadEditableField(UnityObject obj)
    {
        var field = ReadField(obj);
        if (!ReadRaw(obj).AsSpan().SequenceEqual(field.WriteToByteArray(files[obj.File].file.Header.Endianness)))
            throw new UnityFormatException("El esquema no reconstruye todos los bytes. Se permite inspeccionar/exportar RAW, pero se bloquea edición estructurada para evitar pérdida de datos.");
        return field;
    }

    public void ReplaceField(UnityObject obj, AssetTypeValueField field, string description)
        => ReplaceRaw(obj, field.WriteToByteArray(files[obj.File].file.Header.Endianness), description, true);

    public void ReplaceRaw(UnityObject obj, byte[] bytes, string description = "Importación RAW", bool requireSchema = false)
    {
        if (bytes.Length == 0 || bytes.Length > Limits.MaxObjectBytes) throw new UnityFormatException("Tamaño de reemplazo inválido.");
        var (inst, info) = Resolve(obj);
        AssetTypeTemplateField? template = null;
        try { template = Template(inst, info); }
        catch (UnityFormatException) when (!requireSchema) { logs.Add("RAW sin esquema: solo se validará integridad binaria; compatibilidad semántica no comprobada."); }
        if (template != null)
        {
            using var stream = new MemoryStream(bytes, false);
            var reader = new AssetsFileReader(stream) { BigEndian = inst.file.Header.Endianness };
            var parsed = template.MakeValue(reader, manager.GetRefTypeManager(inst));
            if (reader.Position != bytes.Length || !parsed.WriteToByteArray(inst.file.Header.Endianness).AsSpan().SequenceEqual(bytes))
                throw new UnityFormatException("El RAW no coincide exactamente con el esquema serializado del objeto.");
        }
        using var original = OpenRaw(obj, true);
        var change = new Change(obj.Key, description, original.Length, bytes.Length, StreamTools.Hash(original), StreamTools.HashBytes(bytes));
        info.SetNewData(bytes);
        changes[obj.Key] = change;
    }

    public void Revert(UnityObject obj)
    {
        Resolve(obj).info.Replacer = null;
        changes.Remove(obj.Key);
    }

    Stream EntryStream(AssetBundleDirectoryInfo entry)
    {
        if (entry.Replacer != null) { var stream = entry.Replacer.GetPreviewStream(); return new SegmentStream(stream, 0, stream.Length); }
        return new SegmentStream(bundle!.DataStream, entry.Offset, entry.DecompressedSize);
    }
    AssetBundleDirectoryInfo FindEntry(string name) => bundle?.file.BlockAndDirInfo.DirectoryInfos.First(e => e.Name == name)
        ?? throw new InvalidOperationException("El archivo no es un bundle.");

    public void ExtractEntry(string name, Stream output, CancellationToken cancel = default)
    {
        if (files.TryGetValue(name, out var inst) && inst.file.Metadata.AssetInfos.Any(i => i.Replacer != null))
        {
            string path = Path.Combine(temp, Guid.NewGuid() + ".assets");
            try
            {
                using (var writer = new AssetsFileWriter(File.Create(path))) inst.file.Write(writer);
                using var data = File.OpenRead(path); StreamTools.Copy(data, output, data.Length, cancel);
            }
            finally { File.Delete(path); }
        }
        else { using var stream = EntryStream(FindEntry(name)); StreamTools.Copy(stream, output, stream.Length, cancel); }
    }

    public void ReplaceEntry(string name, string replacementPath, CancellationToken cancel = default)
    {
        var entry = FindEntry(name);
        if (changes.Values.Any(c => c.Key.StartsWith(name + ":", StringComparison.Ordinal)))
            throw new InvalidOperationException("Revierte los cambios de objetos de esta entrada antes de reemplazarla completa.");
        string staged = Path.Combine(temp, Guid.NewGuid() + ".replacement");
        using (var source = File.OpenRead(replacementPath))
        {
            if (source.Length > Limits.MaxFileBytes) throw new UnityFormatException("Reemplazo demasiado grande.");
            using var target = File.Create(staged); StreamTools.Copy(source, target, source.Length, cancel);
        }
        AssetsFileInstance? newFile = null;
        if (files.ContainsKey(name) || entry.IsSerialized)
        {
            using (var check = File.OpenRead(staged)) Preflight.Assets(check, Limits);
            newFile = manager.LoadAssetsFile(staged, false); CheckAssets(newFile);
        }
        using var previous = EntryStream(entry);
        changes["entry:" + name] = new("entry:" + name, "Reemplazo de entrada completa", previous.Length,
            new FileInfo(staged).Length, StreamTools.Hash(previous), StreamTools.HashFile(staged));
        var replacement = File.OpenRead(staged); owned.Add(replacement);
        entry.Replacer = new ContentReplacerFromStream(replacement);
        if (newFile != null) files[name] = newFile;
    }

    public void AttachResource(string name, string privatePath) => resources[Path.GetFileName(name)] = privatePath;
    public byte[] ReadResource(string name, ulong offset, uint length)
    {
        if (length > Limits.MaxObjectBytes || offset > long.MaxValue) throw new UnityFormatException("Recurso demasiado grande.");
        string baseName = Path.GetFileName(name.Replace('\\', '/'));
        var entry = bundle?.file.BlockAndDirInfo.DirectoryInfos.FirstOrDefault(e => e.Name == baseName);
        using var stream = entry != null ? EntryStream(entry) : resources.TryGetValue(baseName, out var path)
            ? File.OpenRead(path) : throw new FileNotFoundException("Falta el recurso " + baseName + ". Añádelo con el selector de archivos.");
        if (offset > (ulong)stream.Length || length > (ulong)stream.Length - offset) throw new UnityFormatException("Rango de recurso inválido.");
        stream.Position = (long)offset; byte[] bytes = new byte[length]; stream.ReadExactly(bytes); return bytes;
    }

    public SaveResult SaveAs(string output, BundleCompression compression = BundleCompression.Lz4,
        CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        output = Path.GetFullPath(output);
        if (output == SourcePath || File.Exists(output)) throw new IOException("Guardar como exige un archivo nuevo; el original nunca se sobrescribe.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string staging = output + ".partial-" + Guid.NewGuid().ToString("N");
        string unpacked = Path.Combine(temp, Guid.NewGuid() + ".bundle");
        var restore = new List<(AssetBundleDirectoryInfo, IContentReplacer?)>();
        var streams = new List<Stream>();
        var temporary = new List<string>();
        try
        {
            progress?.Report("Calculando integridad de objetos y entradas…");
            var expected = ObjectHashes(cancel);
            var resourcesExpected = new Dictionary<string, string>();
            if (bundle != null)
            {
                foreach (var e in bundle.file.BlockAndDirInfo.DirectoryInfos)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (files.TryGetValue(e.Name, out var inst))
                    {
                        if (!inst.file.Metadata.AssetInfos.Any(i => i.Replacer != null)) continue;
                        string path = Path.Combine(temp, Guid.NewGuid() + ".assets"); temporary.Add(path);
                        using (var w = new AssetsFileWriter(File.Create(path))) inst.file.Write(w);
                        var stream = File.OpenRead(path); streams.Add(stream);
                        restore.Add((e, e.Replacer)); e.Replacer = new ContentReplacerFromStream(stream);
                    }
                    else { using var raw = EntryStream(e); resourcesExpected.Add(e.Name, StreamTools.Hash(raw, cancel)); }
                }
                progress?.Report("Reconstruyendo bundle…");
                using (var w = new AssetsFileWriter(File.Create(unpacked))) bundle.file.Write(w);
                cancel.ThrowIfCancellationRequested();
                if (compression == BundleCompression.None) File.Move(unpacked, staging);
                else
                {
                    var packed = new AssetBundleFile();
                    packed.Read(new AssetsFileReader(File.OpenRead(unpacked)));
                    try
                    {
                        using var w = new AssetsFileWriter(File.Create(staging));
                        packed.Pack(w, compression == BundleCompression.Lzma ? AssetBundleCompressionType.LZMA : AssetBundleCompressionType.LZ4,
                            blockDirAtEnd: true, progress: new CompressionProgress(cancel, progress));
                    }
                    finally { packed.Close(); }
                }
            }
            else { using var w = new AssetsFileWriter(File.Create(staging)); files["__main__"].file.Write(w); }
            cancel.ThrowIfCancellationRequested();
            progress?.Report("Reabriendo y comparando todos los objetos…");
            using (var verify = Open(staging, temp, classData, cancel, Limits))
            {
                var actual = verify.ObjectHashes(cancel);
                if (actual.Count != expected.Count || expected.Any(e => !actual.TryGetValue(e.Key, out var h) || h != e.Value))
                    throw new UnityFormatException("Validación fallida: cambió un objeto inesperado o no persistió un reemplazo.");
                if (bundle != null && !Entries.Select(e => e.Name).SequenceEqual(verify.Entries.Select(e => e.Name)))
                    throw new UnityFormatException("Validación fallida: cambió el directorio del bundle.");
                foreach (var (name, hash) in resourcesExpected)
                {
                    using var stream = verify.EntryStream(verify.FindEntry(name));
                    if (StreamTools.Hash(stream, cancel) != hash) throw new UnityFormatException("Recurso alterado inesperadamente: " + name);
                }
            }
            string sha = StreamTools.HashFile(staging, cancel);
            using (var flush = new FileStream(staging, FileMode.Open, FileAccess.Write)) flush.Flush(true);
            cancel.ThrowIfCancellationRequested();
            File.Move(staging, output); // same-directory rename, fails if the destination appeared meanwhile
            logs.Add($"Guardado validado: {expected.Count} objetos. SHA256 {sha}");
            return new(output, sha, expected.Count, bundle?.file.BlockAndDirInfo.DirectoryInfos.Count ?? 0);
        }
        finally
        {
            foreach (var (entry, old) in restore) entry.Replacer = old;
            foreach (var stream in streams) stream.Dispose();
            foreach (var path in temporary) File.Delete(path);
            File.Delete(staging); File.Delete(unpacked);
        }
    }

    Dictionary<string, string> ObjectHashes(CancellationToken cancel)
    {
        var hashes = new Dictionary<string, string>();
        foreach (var (name, inst) in files)
            foreach (var info in inst.file.Metadata.AssetInfos)
            {
                cancel.ThrowIfCancellationRequested();
                var obj = new UnityObject(name, info.PathId, info.TypeId, "", "", info.ByteSize, "", "", false);
                using var stream = OpenRaw(obj); hashes.Add(obj.Key, StreamTools.Hash(stream, cancel));
            }
        return hashes;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        manager.UnloadAll(true);
        foreach (var s in owned) s.Dispose();
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }

    sealed class CompressionProgress(CancellationToken cancel, IProgress<string>? progress) : IAssetBundleCompressProgress
    {
        public void SetProgress(float value) { cancel.ThrowIfCancellationRequested(); progress?.Report($"Comprimiendo: {value:P0}"); }
    }
}
