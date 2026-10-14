using Android.OS;
using System.Text.Json;

namespace Uabea.Android;

/// <summary>Separates worker startup failures from Unity parser failures in debug diagnostics.</summary>
static class WorkerProbe
{
    public static Task Run(string directory)
    {
        var completion = new TaskCompletionSource();
        var handler = new Handler(Looper.MainLooper!);
        int pool = 0, managed = 0, java = 0;
        long started = SystemClock.ElapsedRealtime();
        void Enter(string name)
            => global::Android.Util.Log.Info("UabeaSelfTest", "WORKER ENTER: " + name);
        _ = Task.Run(() => { Enter("ThreadPool"); Interlocked.Exchange(ref pool, 1); });
        new System.Threading.Thread(() => { Enter("ManagedThread"); Interlocked.Exchange(ref managed, 1); })
            { IsBackground = true, Name = "UabeaManagedProbe" }.Start();
        new Java.Lang.Thread(() => { Enter("JavaThread"); Interlocked.Exchange(ref java, 1); }, "UabeaJavaProbe").Start();
        void Poll()
        {
            bool passed = Volatile.Read(ref pool) == 1 && Volatile.Read(ref managed) == 1 && Volatile.Read(ref java) == 1;
            if (!passed && SystemClock.ElapsedRealtime() - started < 10000)
            {
                handler.PostDelayed(Poll, 100);
                return;
            }
            ThreadPool.GetAvailableThreads(out int workers, out int ports);
            string json = JsonSerializer.Serialize(new { passed, pool, managed, java, workers, ports,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription });
            File.WriteAllText(Path.Combine(directory, "worker-probe.json"), json);
            global::Android.Util.Log.Info("UabeaSelfTest", "WORKER PROBE: " + json);
            if (passed) completion.SetResult();
            else completion.SetException(new IOException("El runtime no inició todos los hilos de trabajo: " + json));
            handler.Dispose();
        }
        handler.PostDelayed(Poll, 100);
        return completion.Task;
    }
}
