using System.Text;
using Uabea.Core;

namespace Uabea.Android;

/// <summary>Only explicit root actions call su. Never used by ordinary SAF operations.</summary>
public static class RootStorage
{
    static string Quote(string path)
    {
        if (path.IndexOfAny(['\0', '\n', '\r']) >= 0) throw new ArgumentException("Ruta inválida.");
        return "'" + path.Replace("'", "'\"'\"'") + "'";
    }
    static void Absolute(string path)
    {
        if (!path.StartsWith('/')) throw new ArgumentException("Se requiere una ruta absoluta.");
    }
    static async Task<string> Command(string command, CancellationToken cancel)
    {
        using var process = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "su", "-c", command })!;
        using var registration = cancel.Register(() => process.Destroy());
        using var reader = new StreamReader(process.InputStream!);
        using var errors = new StreamReader(process.ErrorStream!);
        Task<string> output = reader.ReadToEndAsync(cancel), error = errors.ReadToEndAsync(cancel);
        int code = await Task.Run(process.WaitFor, cancel);
        await Task.WhenAll(output, error);
        if (code != 0) throw new IOException("Root denegado/error: " + await error);
        return await output;
    }
    public static async Task<bool> RequestAccess(CancellationToken cancel) => (await Command("id -u", cancel)).Trim() == "0";
    public static async Task<string> Import(string source, string privateDirectory, CancellationToken cancel)
    {
        Absolute(source);
        string destination = Path.Combine(privateDirectory, "root-" + Guid.NewGuid().ToString("N") + ".bin");
        using var process = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "su", "-c", "test -f " + Quote(source) + " && cat " + Quote(source) })!;
        using var registration = cancel.Register(() => process.Destroy());
        using var errors = new StreamReader(process.ErrorStream!);
        Task<string> error = errors.ReadToEndAsync(cancel);
        string? copied = null;
        try
        {
            copied = await Uabea.Storage.WorkspaceStorage.ImportAsync(process.InputStream!, privateDirectory, Path.GetFileName(source), cancel);
            int exit = await Task.Run(process.WaitFor, cancel);
            if (exit != 0) throw new IOException(await error);
            return copied;
        }
        catch { if (copied != null) File.Delete(copied); throw; }
    }
    public static async Task<string> ExportConfirmed(string validatedSource, string destination, CancellationToken cancel)
    {
        Absolute(destination);
        string backup = destination + ".uabea-backup-" + Guid.NewGuid().ToString("N");
        string staging = destination + ".uabea-tmp-" + Guid.NewGuid().ToString("N");
        string hash = StreamTools.HashFile(validatedSource, cancel).ToLowerInvariant();
        string src = Quote(validatedSource), dst = Quote(destination), tmp = Quote(staging), bak = Quote(backup);
        // No network, no game process control. Backup and staging stay in the target directory.
        string script = "set -eu\n" +
            "test ! -L " + dst + "\n" +
            "trap 'rm -f " + tmp.Replace("'", "'\"'\"'") + "' EXIT\n" +
            "if test -e " + dst + "; then cp -p " + dst + " " + bak + "; cp -p " + dst + " " + tmp + "; fi\n" +
            "cat " + src + " > " + tmp + "\n" +
            "test \"$(sha256sum " + tmp + " | cut -d ' ' -f 1)\" = " + Quote(hash) + "\n" +
            "mv " + tmp + " " + dst + "\n";
        await Command(script, cancel);
        return backup;
    }
}
