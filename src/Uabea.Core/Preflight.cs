using AssetsTools.NET;
using System.Text;

namespace Uabea.Core;

internal static class Preflight
{
    public static void Bundle(Stream stream, DocumentLimits limits)
    {
        var reader = new AssetsFileReader(stream) { BigEndian = true };
        string CString(int max)
        {
            var chars = new List<byte>();
            for (int i = 0; i < max; i++) { byte b = reader.ReadByte(); if (b == 0) return Encoding.UTF8.GetString(chars.ToArray()); chars.Add(b); }
            throw new UnityFormatException("Cadena de encabezado demasiado larga.");
        }
        if (CString(16) != "UnityFS") throw new UnityFormatException("No es UnityFS estándar.");
        uint version = reader.ReadUInt32();
        if (version < 6 || version > 8) throw new UnityFormatException($"Versión de bundle {version} no compatible.");
        CString(128); CString(128);
        long size = reader.ReadInt64();
        uint packed = reader.ReadUInt32(), expanded = reader.ReadUInt32(), flags = reader.ReadUInt32();
        if (size != stream.Length || packed == 0 || packed > stream.Length || expanded > limits.MaxMetadataBytes || (flags & 0x3f) > 3)
            throw new UnityFormatException("Encabezado UnityFS dañado, cifrado o no estándar (tamaños/compresión inválidos).");
        stream.Position = 0;
    }

    public static void BundleRanges(AssetBundleFile file, DocumentLimits limits)
    {
        long expanded = 0, packed = 0;
        foreach (var block in file.BlockAndDirInfo.BlockInfos)
        {
            if ((block.Flags & 0x3f) > 3 || block.DecompressedSize > 512 * 1024 * 1024)
                throw new UnityFormatException("Bloque demasiado grande o compresión no compatible.");
            expanded = checked(expanded + block.DecompressedSize);
            packed = checked(packed + block.CompressedSize);
        }
        if (expanded > limits.MaxExpandedBytes || packed > file.Reader.BaseStream.Length)
            throw new UnityFormatException("El bundle excede los límites o declara bloques truncados.");
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (var e in file.BlockAndDirInfo.DirectoryInfos)
            if (!names.Add(e.Name) || e.Offset < 0 || e.DecompressedSize < 0 || e.Offset > expanded || e.DecompressedSize > expanded - e.Offset)
                throw new UnityFormatException("Directorio de bundle inválido.");
    }

    public static void Assets(Stream stream, DocumentLimits limits)
    {
        if (stream.Length < 20) throw new UnityFormatException("Archivo serializado truncado.");
        var header = new AssetsFileHeader(); header.Read(new AssetsFileReader(stream));
        if (header.Version < 13 || header.Version > 23 || header.FileSize != stream.Length || header.DataOffset < header.GetSize()
            || header.DataOffset > stream.Length || header.MetadataSize < 0 || header.MetadataSize > limits.MaxMetadataBytes
            || header.MetadataSize > stream.Length - header.GetSize())
            throw new UnityFormatException("No es un SerializedFile compatible (versiones 13–23), o está truncado/cifrado.");
        stream.Position = 0;
    }
}
