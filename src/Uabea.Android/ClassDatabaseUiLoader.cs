using System.IO.Compression;
using Uabea.Core;

namespace Uabea.Android;

/// <summary>
/// Incremental class database installer: at most 16 KiB per UI event loop step.
/// Avoids managed Thread.Start / Task.Run, whose worker callbacks were not entered
/// on the user's Android 16 device or the Android CI emulator.
/// </summary>
public sealed class ClassDatabaseUiLoader : IDisposable
{
    const string Expected = "dcea167eb5e8d17e7aa33c933a7332e10f88642f7e6e791b0cc2a6b7f44ec247";
    const int ChunkSize = 16 * 1024;

    readonly string apkPath;
    readonly string destination;
    readonly byte[] buffer = new byte[ChunkSize];
    readonly ManagedSha256 hash = new();
    FileStream? existing;
    ZipArchive? apk;
    Stream? compressed;
    FileStream? stagingOutput;
    string? staging;
    long done;
    long total;
    int stage; // 0 inspect; 1 verify existing; 2 extract; 3 verified
    bool disposed;

    public ClassDatabaseProgress State { get; private set; } = new("Abriendo archivo instalado", 0, 0);
    public bool Completed => stage == 3;

    public ClassDatabaseUiLoader(string apkPath, string destination)
    {
        this.apkPath = apkPath;
        this.destination = destination;
    }

    public bool Step()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ClassDatabaseUiLoader));
        if (Completed) return true;
        if (stage == 0)
        {
            if (File.Exists(destination))
            {
                existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                done = 0; total = existing.Length; stage = 1;
                State = new("Verificando base existente", 0, total);
            }
            else BeginExtraction();
            return false;
        }
        if (stage == 1)
        {
            int n = existing!.Read(buffer, 0, buffer.Length);
            if (n > 0)
            {
                hash.AppendData(buffer, 0, n);
                done += n; State = new("Verificando base existente", done, total);
                return false;
            }
            bool valid = hash.GetHashHexAndReset().Equals(Expected, StringComparison.OrdinalIgnoreCase);
            existing.Dispose(); existing = null;
            if (valid)
            {
                stage = 3; State = new("Lista: integridad confirmada", total, total);
                return true;
            }
            BeginExtraction(); return false;
        }
        if (stage == 2)
        {
            int n = compressed!.Read(buffer, 0, buffer.Length);
            if (n > 0)
            {
                stagingOutput!.Write(buffer, 0, n);
                hash.AppendData(buffer, 0, n);
                done += n; State = new("Extrayendo base Unity", done, total);
                return false;
            }
            string result = hash.GetHashHexAndReset();
            if (done != total || !result.Equals(Expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La base de clases empaquetada no pasó la verificación SHA-256.");
            stagingOutput!.Flush(true);
            CloseStreams();
            File.Move(staging!, destination, true);
            staging = null; stage = 3;
            State = new("Lista: integridad confirmada", total, total);
            return true;
        }
        throw new InvalidOperationException("Estado inesperado del lector Unity.");
    }

    void BeginExtraction()
    {
        State = new("Abriendo datos Unity en APK", 0, 0);
        apk = ZipFile.OpenRead(apkPath);
        var entry = apk.GetEntry("assets/classdata.tpk")
            ?? throw new FileNotFoundException("No se encontró classdata.tpk dentro de la APK.");
        total = entry.Length; done = 0;
        compressed = entry.Open();
        staging = destination + ".partial-" + Guid.NewGuid().ToString("N");
        stagingOutput = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stage = 2;
        State = new("Extrayendo base Unity", 0, total);
    }

    void CloseStreams()
    {
        existing?.Dispose(); existing = null;
        stagingOutput?.Dispose(); stagingOutput = null;
        compressed?.Dispose(); compressed = null;
        apk?.Dispose(); apk = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CloseStreams();
        if (staging != null)
        {
            try { File.Delete(staging); } catch (IOException) { }
        }
    }
}
