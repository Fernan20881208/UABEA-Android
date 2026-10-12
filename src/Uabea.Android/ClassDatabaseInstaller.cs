using Android.Content.Res;
using Uabea.Core;

namespace Uabea.Android;

static class ClassDatabaseInstaller
{
    const string Expected = "dcea167eb5e8d17e7aa33c933a7332e10f88642f7e6e791b0cc2a6b7f44ec247";
    // Java asset streams are synchronous. Read them on a worker, not through CopyToAsync on the UI context.
    public static Task Ensure(AssetManager assets, string destination, CancellationToken cancel) => Task.Run(() =>
    {
        if (File.Exists(destination) && StreamTools.HashFile(destination, cancel).Equals(Expected, StringComparison.OrdinalIgnoreCase)) return;
        string staging = destination + ".partial";
        try
        {
            using (var source = assets.Open("classdata.tpk"))
            using (var target = File.Create(staging))
            {
                byte[] buffer = new byte[65536]; int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested(); target.Write(buffer, 0, count);
                }
                target.Flush(true);
            }
            if (!StreamTools.HashFile(staging, cancel).Equals(Expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La base de clases empaquetada está incompleta o dañada.");
            File.Move(staging, destination, true);
        }
        finally { File.Delete(staging); }
    }, cancel);
}
