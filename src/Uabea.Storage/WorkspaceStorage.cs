using Uabea.Core;

namespace Uabea.Storage;

public static class WorkspaceStorage
{
    /// <summary>Works with non-seekable SAF/cloud streams. Leaves no partial file on failure.</summary>
    public static async Task<string> ImportAsync(Stream source, string directory, string name,
        CancellationToken cancel = default, IProgress<long>? progress = null, long maxBytes = 2L * 1024 * 1024 * 1024)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + "-" + StreamTools.SafeName(name));
        string staging = path + ".partial";
        try
        {
            await using (var destination = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                byte[] buffer = new byte[131072]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, cancel)) != 0)
                {
                    total += read;
                    if (total > maxBytes) throw new IOException("El archivo supera el límite de tamaño.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancel);
                    progress?.Report(total);
                }
                await destination.FlushAsync(cancel); destination.Flush(true);
            }
            cancel.ThrowIfCancellationRequested(); File.Move(staging, path); return path;
        }
        finally { File.Delete(staging); }
    }

    /// <summary>Caller opens a newly created destination; original identity must be rejected by platform adapter.</summary>
    public static async Task ExportAsync(string sourcePath, Func<Stream> openWrite, Func<Stream> openRead,
        CancellationToken cancel = default)
    {
        string expected = StreamTools.HashFile(sourcePath, cancel);
        await using (var input = File.OpenRead(sourcePath))
        await using (var output = openWrite())
        {
            await input.CopyToAsync(output, 131072, cancel);
            await output.FlushAsync(cancel);
        }
        // Providers are not necessarily atomic. Read-back is required, and private validated output remains available.
        using var verify = openRead();
        if (StreamTools.Hash(verify, cancel) != expected) throw new IOException("El proveedor guardó datos incompletos o alterados. Conserva la copia privada y exporta de nuevo.");
    }
}
