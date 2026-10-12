using Android.Content;
using Android.Provider;
using Uabea.Storage;
using Uri = Android.Net.Uri;

namespace Uabea.Android;

public sealed class SafStorage(Context context, string directory)
{
    public string DisplayName(Uri uri)
    {
        using var cursor = context.ContentResolver!.Query(uri, [OpenableColumns.DisplayName], null, null, null);
        return cursor != null && cursor.MoveToFirst() ? cursor.GetString(0) ?? "selected.bin" : "selected.bin";
    }
    public async Task<string> ImportAsync(Uri uri, CancellationToken cancel, IProgress<long>? progress = null)
    {
        using var source = context.ContentResolver!.OpenInputStream(uri) ?? throw new IOException("El proveedor no permite lectura.");
        return await WorkspaceStorage.ImportAsync(source, directory, DisplayName(uri), cancel, progress);
    }
    public Task ExportAsync(string source, Uri destination, Uri? original, CancellationToken cancel)
    {
        if (original != null && destination.Equals(original)) throw new IOException("No se permite sobrescribir el URI del original.");
        return WorkspaceStorage.ExportAsync(source,
            () => context.ContentResolver!.OpenOutputStream(destination, "wt") ?? throw new IOException("No se pudo escribir."),
            () => context.ContentResolver!.OpenInputStream(destination) ?? throw new IOException("No se pudo verificar la exportación."), cancel);
    }
}
