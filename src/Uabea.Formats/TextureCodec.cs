using AssetsTools.NET.Texture;
using StbImageSharp;
using Uabea.Core;
using System.Buffers.Binary;

namespace Uabea.Formats;

public sealed record TextureInfo(int Width, int Height, int MipCount, string Format, bool Streamed);

public static class TextureCodec
{
    public const int MaxPixels = 4 * 1024 * 1024;
    static TextureFile Read(UnityDocument document, UnityObject obj, bool editable = false)
    {
        if (obj.ClassId != 28) throw new ArgumentException("Selecciona un Texture2D.");
        var root = editable ? document.ReadEditableField(obj) : document.ReadField(obj);
        var tex = TextureFile.ReadTextureFile(root);
        CheckDimensions(tex.m_Width, tex.m_Height);
        if (tex.m_ImageCount > 1 || tex.m_TextureDimension != 2) throw new UnityFormatException("Solo texturas 2D de una imagen; arrays/cubemaps no compatibles.");
        if (tex.m_PlatformBlob is { Length: > 0 }) throw new UnityFormatException("Textura con PlatformBlob/swizzle: no se modifica ni decodifica en esta versión.");
        return tex;
    }

    public static TextureInfo Info(UnityDocument document, UnityObject obj)
    {
        var tex = Read(document, obj);
        return new(tex.m_Width, tex.m_Height, tex.m_MipCount, ((TextureFormat)tex.m_TextureFormat).ToString(), tex.m_StreamData.size > 0);
    }
    static void CheckDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 4096 || (long)width * height > MaxPixels)
            throw new UnityFormatException("Textura fuera del límite: 4096 por eje y 4 megapíxeles en total.");
    }

    public static byte[] ExportPng(UnityDocument document, UnityObject obj)
    {
        var tex = Read(document, obj);
        var bytes = tex.pictureData;
        if (bytes == null || bytes.Length == 0)
            bytes = document.ReadResource(tex.m_StreamData.path, tex.m_StreamData.offset, tex.m_StreamData.size);
        using var png = new MemoryStream();
        if (!tex.DecodeTextureImage(bytes, png, ImageExportType.Png))
            throw new UnityFormatException("El códec no soporta este formato de textura.");
        return png.ToArray();
    }

    /// <summary>Explicit RGBA32 conversion; never calls native desktop encoders.</summary>
    public static void ImportPngRgba32(UnityDocument document, UnityObject obj, byte[] png, bool preserveMipCount = true)
    {
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new FormatException("Se requiere un PNG.");
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        CheckDimensions(width, height);
        var root = document.ReadEditableField(obj);
        var tex = Read(document, obj);
        if (width != tex.m_Width || height != tex.m_Height)
            throw new UnityFormatException($"El PNG debe medir {tex.m_Width} × {tex.m_Height}; no se cambian las dimensiones.");
        var decoded = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        if (decoded.Width != width || decoded.Height != height) throw new FormatException("PNG inconsistente.");
        int maxMips = 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));
        int count = preserveMipCount ? Math.Clamp(tex.m_MipCount, 1, maxMips) : 1;
        byte[] pixels = decoded.Data;
        using var encoded = new MemoryStream();
        for (int mip = 0, w = width, h = height; mip < count; mip++)
        {
            // Unity stores bottom row first. Input pixels are top row first.
            for (int y = h - 1; y >= 0; y--) encoded.Write(pixels, y * w * 4, w * 4);
            if (mip + 1 < count)
            {
                pixels = Downsample(pixels, w, h);
                w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
            }
        }
        tex.SetPictureData(encoded.ToArray(), width, height, TextureFormat.RGBA32, count);
        tex.WriteTo(root);
        document.ReplaceField(obj, root, $"PNG → RGBA32, {count} mip(s); filtros de mip en espacio de bytes");
    }

    static byte[] Downsample(byte[] data, int width, int height)
    {
        int w = Math.Max(1, width / 2), h = Math.Max(1, height / 2);
        byte[] next = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) for (int c = 0; c < 4; c++)
        {
            int sum = 0;
            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                sum += data[(Math.Min(height - 1, y * 2 + dy) * width + Math.Min(width - 1, x * 2 + dx)) * 4 + c];
            next[(y * w + x) * 4 + c] = (byte)((sum + 2) / 4);
        }
        return next;
    }
}
