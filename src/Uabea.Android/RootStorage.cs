using System.Text;
using Uabea.Core;
using Uabea.Storage;

namespace Uabea.Android;

/// <summary>Only explicit root actions call su. Never used by ordinary SAF operations.</summary>
public static class RootStorage
{
    static async Task<string> Command(string command, CancellationToken cancel)
    {
        using var process = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "su", "-c", command })!;
        using var registration = cancel.Register(() => process.Destroy());
        using var reader = new StreamReader(process.InputStream!);
        using var errors = new StreamReader(process.ErrorStream!);
        Task<string> output = reader.ReadToEndAsync(cancel), error = errors.ReadToEndAsync(cancel);
        int code = await Task.Run(process.WaitFor, cancel);
        await Task.WhenAll(output, error);
        cancel.ThrowIfCancellationRequested();
        if (code != 0) throw new IOException("Root denegado/error: " + await error);
        return await output;
    }
    public static async Task<bool> RequestAccess(CancellationToken cancel) => (await Command("id -u", cancel)).Trim() == "0";
    public static async Task<string> Diagnose(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            if (!await RequestAccess(timeout.Token)) return "Root: su no devolvió UID 0.";
            return "Root: concedido (UID 0).\n" + await Command("id; getenforce; for tool in cat cp mv rm sha256sum cut stat ls chcon; do command -v \"$tool\" || exit 1; done", timeout.Token) +
                "\nEste diagnóstico no escribe archivos protegidos. El acceso a cada ruta depende de su montaje y política SELinux.";
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new IOException("La solicitud root agotó 45 segundos. Revisa KernelSU/SukiSU y vuelve a intentarlo."); }
    }
    public static async Task<string> Import(string source, string privateDirectory, CancellationToken cancel)
    {
        using var process = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "su", "-c", RootCommands.Read(source) })!;
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
        catch { process.Destroy(); if (copied != null) File.Delete(copied); throw; }
    }
    public static async Task<string> ExportConfirmed(string validatedSource, string destination, CancellationToken cancel)
    {
        string hash = StreamTools.HashFile(validatedSource, cancel).ToLowerInvariant();
        var plan = RootCommands.Export(validatedSource, destination, hash);
        if (!await RequestAccess(cancel)) throw new IOException("No se concedió root.");
        return await Command(plan.Script, cancel);
    }
}
