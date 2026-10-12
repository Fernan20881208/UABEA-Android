using System.IO.Compression;
using System.Security.Cryptography;
using Uabea.Core;

namespace Uabea.Android;

static class ClassDatabaseInstaller
{
    const string Expected = "dcea167eb5e8d17e7aa33c933a7332e10f88642f7e6e791b0cc2a6b7f44ec247";
    // Read directly from the signed APK using managed streams.
    // Avoids Android AssetManager JNI reads during startup.
    public static void EnsureSync(string apkPath, string destination, CancellationToken cancel)
    {
        if (File.Exists(destination) && StreamTools.HashFile(destination, cancel).Equals(Expected, StringComparison.OrdinalIgnoreCase)) return;
        string staging = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var apk = ZipFile.OpenRead(apkPath))
            using (var source = (apk.GetEntry("assets/classdata.tpk") ?? throw new FileNotFoundException("classdata.tpk no existe dentro de la APK")).Open())
            using (var target = File.Create(staging))
            {
                byte[] buffer = new byte[65536]; int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    target.Write(buffer, 0, count);
                    checksum.AppendData(buffer, 0, count);
                }
                target.Flush(true);
            }
            // Do not reopen the temporary file for hashing: Android/.NET can still hold an
            // exclusive FileStream share lock immediately after close (observed launch crash).
            if (!Convert.ToHexString(checksum.GetHashAndReset()).Equals(Expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La base de clases empaquetada está incompleta o dañada.");
            File.Move(staging, destination, true);
        }
        finally { File.Delete(staging); }
    }
}
