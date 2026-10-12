using System.Security.Cryptography;

namespace Uabea.Core;

public static class StreamTools
{
    public static void Copy(Stream source, Stream target, long length, CancellationToken cancel = default)
    {
        byte[] buffer = new byte[128 * 1024];
        while (length > 0)
        {
            cancel.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, (int)Math.Min(length, buffer.Length));
            if (read == 0) throw new EndOfStreamException("Datos truncados.");
            target.Write(buffer, 0, read);
            length -= read;
        }
    }

    public static string Hash(Stream input, CancellationToken cancel = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            cancel.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static string HashFile(string path, CancellationToken cancel = default)
    {
        using var input = File.OpenRead(path);
        return Hash(input, cancel);
    }
    public static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string SafeName(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
}
