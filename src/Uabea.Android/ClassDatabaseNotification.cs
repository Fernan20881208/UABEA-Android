using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Uabea.Android;

/// <summary>
/// Low-importance notification for the actual class database bytes being read.
/// No foreground service is needed: the app initializes this small file on launch.
/// </summary>
public sealed class ClassDatabaseNotification
{
    const string ChannelId = "uabea_unity_classdata";
    const int NotificationId = 1107;
    readonly Activity activity;
    readonly NotificationManager? manager;

    public ClassDatabaseNotification(Activity activity)
    {
        this.activity = activity;
        manager = activity.GetSystemService(Context.NotificationService) as NotificationManager;
        try
        {
            manager?.CreateNotificationChannel(
                new NotificationChannel(ChannelId, "Preparación de clases Unity", NotificationImportance.Low));
        }
        catch (Exception e) { global::Android.Util.Log.Warn("Uabea", "Notification channel unavailable: " + e.Message); }
    }

    bool Allowed()
    {
        if (manager == null) return false;
        if ((int)Build.VERSION.SdkInt >= 33 &&
            activity.CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted) return false;
        return manager.AreNotificationsEnabled();
    }

    Notification.Builder Builder()
    {
        var intent = new Intent(activity, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var tap = PendingIntent.GetActivity(activity, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        return new Notification.Builder(activity, ChannelId)
            .SetSmallIcon(Resource.Drawable.app_icon)
            .SetContentIntent(tap)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false);
    }

    void Publish(Func<Notification.Builder, Notification.Builder> configure)
    {
        try
        {
            if (!Allowed()) return;
            using var builder = Builder();
            using var notification = configure(builder).Build();
            manager!.Notify(NotificationId, notification);
        }
        catch (Exception e)
        {
            // Denied notification permission must never crash or block the editor.
            global::Android.Util.Log.Warn("Uabea", "Notification unavailable: " + e.Message);
        }
    }

    public void Progress(ClassDatabaseProgress progress)
    {
        int percent = progress.Percent;
        string remaining = (progress.RemainingBytes / 1024.0).ToString("0.0");
        bool hasTotal = progress.BytesTotal > 0;
        Publish(builder => builder
            .SetContentTitle("Cargando clases de Unity")
            .SetContentText(hasTotal
                ? $"{percent}% · faltan {remaining} KiB · {progress.Phase}"
                : progress.Phase + " · esperando lectura")
            .SetProgress(hasTotal ? 100 : 0, hasTotal ? percent : 0, !hasTotal)
            .SetOngoing(true));
    }

    public void Stalled(string phase) => Publish(builder => builder
        .SetContentTitle("La carga Unity no avanza")
        .SetContentText("15 s sin datos · " + phase + ". Abre UABEA para reintentar.")
        .SetProgress(0, 0, true)
        .SetOngoing(false));

    public void Completed() => Publish(builder => builder
        .SetContentTitle("Base de clases Unity lista")
        .SetContentText("100% · Integridad verificada. Ya puedes editar assets.")
        .SetProgress(0, 0, false)
        .SetOngoing(false)
        .SetAutoCancel(true));

    public void Failed(string reason) => Publish(builder => builder
        .SetContentTitle("Error al preparar clases Unity")
        .SetContentText(reason.Length > 120 ? reason[..120] : reason)
        .SetProgress(0, 0, false)
        .SetOngoing(false)
        .SetAutoCancel(true));
}
