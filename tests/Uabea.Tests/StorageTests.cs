using System.Diagnostics;
using Uabea.Core;
using Uabea.Storage;

static class StorageTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }
    static int Shell(string script, string directory)
    {
        var info = new ProcessStartInfo("sh") { WorkingDirectory = directory, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-c"); info.ArgumentList.Add(script);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("Shell test timed out"); }
        Task.WaitAll(error, output);
        return process.ExitCode;
    }
    public static void Register(Action<string, Action> test, string root)
    {
        string directory = Path.Combine(root, "storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        test("Storage non-seekable import, byte limit and cancellation cleanup", () =>
        {
            byte[] bytes = Enumerable.Range(0, 513).Select(i => (byte)i).ToArray();
            using var stream = new NonSeekable(bytes);
            string file = WorkspaceStorage.ImportAsync(stream, directory, "input.bin").GetAwaiter().GetResult();
            Check(File.ReadAllBytes(file).SequenceEqual(bytes), "Non-seekable stream changed");
            using var tooLarge = new NonSeekable(bytes);
            Throws<IOException>(() => WorkspaceStorage.ImportAsync(tooLarge, directory, "large", maxBytes: 512).GetAwaiter().GetResult());
            using var cancelled = new NonSeekable(bytes);
            Throws<OperationCanceledException>(() => WorkspaceStorage.ImportAsync(cancelled, directory, "cancelled", new CancellationToken(true)).GetAwaiter().GetResult());
            Check(Directory.GetFiles(directory).Length == 1, "Partial import leaked");
        });
        test("Storage export read-back detects provider corruption", () =>
        {
            string source = Path.Combine(directory, "source.bin"); File.WriteAllText(source, "validated");
            string output = Path.Combine(directory, "output.bin");
            WorkspaceStorage.ExportAsync(source, () => File.Create(output), () => File.OpenRead(output)).GetAwaiter().GetResult();
            Check(StreamTools.HashFile(source) == StreamTools.HashFile(output), "Export mismatch");
            Throws<IOException>(() => WorkspaceStorage.ExportAsync(source, () => File.Create(output), () => new MemoryStream([1,2,3])).GetAwaiter().GetResult());
            Check(File.ReadAllText(source) == "validated", "Source modified");
        });
        test("Root command arguments reject invalid paths and same source", () =>
        {
            Throws<ArgumentException>(() => RootCommands.Read("relative/path"));
            Throws<ArgumentException>(() => RootCommands.Read("/tmp/file\nrm"));
            Throws<ArgumentException>(() => RootCommands.Export("/tmp/source", "/tmp/dest", "bad"));
            Throws<IOException>(() => RootCommands.Export("/tmp/source", "/tmp/./source", new string('0', 64)));
        });
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Root shell integration tests require Linux; they do not test Android su authorization.");
        test("Root export script preserves backup, permissions and literal shell characters", () =>
        {
            string source = Path.Combine(directory, "source ' with spaces.bin"), destination = Path.Combine(directory, "target ' $(touch injected-marker).bin");
            File.WriteAllText(source, "new validated bytes"); File.WriteAllText(destination, "old original");
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var mode = File.GetUnixFileMode(destination);
            var plan = RootCommands.Export(source, destination, StreamTools.HashFile(source));
            Check(Shell(plan.Script, directory) == 0, "Export script failed");
            Check(File.ReadAllText(destination) == "new validated bytes", "Replacement did not persist");
            Check(File.ReadAllText(plan.Backup) == "old original", "Backup corrupted");
            Check(File.GetUnixFileMode(destination) == mode && File.GetUnixFileMode(plan.Backup) == mode, "Permissions changed");
            Check(!File.Exists(plan.Staging) && !File.Exists(Path.Combine(directory, "injected-marker")), "Temporary file or shell injection");
        });
        test("Root export hash failure and symlink rejection preserve original", () =>
        {
            string source = Path.Combine(directory, "hash-source"), destination = Path.Combine(directory, "hash-target");
            File.WriteAllText(source, "new"); File.WriteAllText(destination, "keep");
            var plan = RootCommands.Export(source, destination, new string('0',64));
            Check(Shell(plan.Script, directory) != 0, "Invalid SHA was accepted");
            Check(File.ReadAllText(destination) == "keep" && !File.Exists(plan.Staging), "Failed write damaged original");
            string link = Path.Combine(directory, "symlink"); File.CreateSymbolicLink(link, destination);
            plan = RootCommands.Export(source, link, StreamTools.HashFile(source));
            Check(Shell(plan.Script, directory) != 0 && File.ReadAllText(destination) == "keep", "Symlink was followed");
        });
        test("Root export to a new target verifies bytes without inventing a backup", () =>
        {
            string source = Path.Combine(directory, "new-source"), destination = Path.Combine(directory, "new-target");
            File.WriteAllText(source, "new bytes");
            var plan = RootCommands.Export(source, destination, StreamTools.HashFile(source));
            Check(Shell(plan.Script, directory) == 0, "New target export failed");
            Check(StreamTools.HashFile(source) == StreamTools.HashFile(destination), "Read-back failed");
            Check(!File.Exists(plan.Backup) && !File.Exists(plan.Staging), "Incorrect backup or staging");
        });
    }
    sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}
