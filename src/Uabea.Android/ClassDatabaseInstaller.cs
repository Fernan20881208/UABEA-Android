using System.IO.Compression;
using System.Security.Cryptography;

namespace Uabea.Android;

/// <summary>Real byte-count progress; phases are separate to avoid invented time estimates.</summary>
public readonly record struct ClassDatabaseProgress(string Phase, long BytesDone, long BytesTotal)
{
    public int Percent => BytesTotal <= 0 ? 0 : (int)Math.Clamp(BytesDone * 100 / BytesTotal, 0, 100);
    public long RemainingBytes => Math.Max(0, BytesTotal - BytesDone);
}

static class ClassDatabaseInstaller
{
    const string Expected = "dcea167eb5e8d17e7aa33c933a7332e10f88642f7e6e791b0cc2a6b7f44ec247";

    static string HashReading(Stream source, string phase, CancellationToken cancel,
        Action<ClassDatabaseProgress>? progress, Stream? copyTo = null)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = source.Length;
        long copied = 0;
        progress?.Invoke(new ClassDatabaseProgress(phase, 0, total));
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancel.ThrowIfCancellationRequested();
            copyTo?.Write(buffer, 0, count);
            digest.AppendData(buffer, 0, count);
            copied += count;
            progress?.Invoke(new ClassDatabaseProgress(phase, copied, total));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    public static void EnsureSync(string apkPath, string destination, CancellationToken cancel,
        Action<ClassDatabaseProgress>? progress = null)
    {
        // Verify an already-extracted class database too: on later starts the
        // percentage reflects bytes hashed, not a pretend download.
        if (File.Exists(destination))
        {
            using var installed = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            string existingHash = HashReading(installed, "Comprobando archivo existente", cancel, progress);
            if (existingHash.Equals(Expected, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Invoke(new ClassDatabaseProgress("Lista", installed.Length, installed.Length));
                return;
            }
        }

        string staging = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var apk = ZipFile.OpenRead(apkPath))
            using (var source = (apk.GetEntry("assets/classdata.tpk")
                ?? throw new FileNotFoundException("classdata.tpk no existe dentro de la APK")).Open())
            using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                string hash = HashReading(source, "Extrayendo base de Unity", cancel, progress, target);
                target.Flush(true);
                progress?.Invoke(new ClassDatabaseProgress("Verificando integridad", source.Length, source.Length));
                if (!hash.Equals(Expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("La base de clases empaquetada está incompleta o dañada.");
            }
            cancel.ThrowIfCancellationRequested();
            File.Move(staging, destination, true);
            long size = new FileInfo(destination).Length;
            progress?.Invoke(new ClassDatabaseProgress("Lista", size, size));
        }
        finally { File.Delete(staging); }
    }
}
