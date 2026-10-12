using Android.Content;
using Android.OS;
using System.Text;
using Uabea.Core;
using Uabea.Storage;
using Uri = Android.Net.Uri;

namespace Uabea.Android;

public static class StorageDiagnostics
{
    public static async Task<string> Check(Context context, string work, Uri? selected, CancellationToken cancel)
    {
        string directory = Path.Combine(work, "permission-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var report = new StringBuilder($"Android {(int)Build.VERSION.SdkInt} · {string.Join(", ", Build.SupportedAbis ?? [])}\nPaquete: {context.PackageName}\n");
        try
        {
            byte[] sample = Encoding.UTF8.GetBytes("UABEA almacenamiento privado");
            using var input = new MemoryStream(sample);
            string copied = await WorkspaceStorage.ImportAsync(input, directory, "probe.bin", cancel);
            if (StreamTools.HashFile(copied, cancel) != StreamTools.HashBytes(sample)) throw new IOException("La lectura privada no coincide.");
            string output = Path.Combine(directory, "export.bin");
            await WorkspaceStorage.ExportAsync(copied, () => File.Create(output), () => File.OpenRead(output), cancel);
            report.AppendLine("Espacio privado: escritura, lectura y SHA-256 correctos.");
            var stats = new StatFs(work);
            report.AppendLine($"Espacio disponible: {stats.AvailableBytes / 1048576:N0} MiB.");
            report.AppendLine("SAF: acceso por archivo elegido; no requiere permiso global de almacenamiento.");
            report.AppendLine($"Permisos URI persistentes: {context.ContentResolver!.PersistedUriPermissions?.Count ?? 0}.");
            if (selected != null)
            {
                using var source = context.ContentResolver.OpenInputStream(selected) ?? throw new IOException("No se pudo abrir el URI seleccionado.");
                byte[] one = new byte[1]; _ = await source.ReadAsync(one, cancel);
                report.AppendLine("Archivo SAF actual: lectura concedida y apertura comprobada.");
            }
            else report.AppendLine("Archivo SAF actual: ninguno; usa Abrir para comprobar un proveedor real.");
            report.AppendLine("Guardado SAF: se verifica al usar Guardar como, releyendo el archivo creado.");
            report.AppendLine("Android/data y Android/obb están restringidos por el selector del sistema. Para rutas protegidas usa Abrir con root.");
            report.AppendLine("Root: no solicitado por este diagnóstico. Usa Comprobar root para pedir autorización al gestor.");
            return report.ToString();
        }
        finally { Directory.Delete(directory, true); }
    }
}
