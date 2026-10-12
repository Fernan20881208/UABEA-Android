namespace Uabea.Storage;

public sealed record RootExportPlan(string Script, string Backup, string Staging);

/// <summary>Portable, tested shell construction. Executing su remains in the Android adapter.</summary>
public static class RootCommands
{
    public static string Quote(string value)
    {
        if (value.IndexOfAny(['\0', '\n', '\r']) >= 0) throw new ArgumentException("Ruta inválida.");
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }
    public static void Absolute(string path)
    {
        if (!path.StartsWith('/') || path.Length < 2) throw new ArgumentException("Se requiere una ruta absoluta de archivo.");
        _ = Quote(path);
    }
    public static string Read(string path)
    {
        Absolute(path);
        return "test -f " + Quote(path) + " && test -r " + Quote(path) + " && cat " + Quote(path);
    }
    public static RootExportPlan Export(string source, string destination, string expectedHash)
    {
        Absolute(source); Absolute(destination);
        if (Path.GetFullPath(source) == Path.GetFullPath(destination)) throw new IOException("Origen y destino son el mismo archivo.");
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)) throw new ArgumentException("SHA-256 inválido.");
        string backup = destination + ".uabea-backup-" + Guid.NewGuid().ToString("N");
        string staging = destination + ".uabea-tmp-" + Guid.NewGuid().ToString("N");
        string script = "set -eu\n" +
            "src=" + Quote(source) + "\ndst=" + Quote(destination) + "\ntmp=" + Quote(staging) + "\nbak=" + Quote(backup) + "\nexpected=" + Quote(expectedHash.ToLowerInvariant()) + "\n" +
            "test -f \"$src\"\ntest ! -L \"$dst\"\ntest ! -e \"$dst\" || test -f \"$dst\"\n" +
            "test ! -e \"$tmp\" && test ! -L \"$tmp\" && test ! -e \"$bak\" && test ! -L \"$bak\"\n" +
            "umask 077\ntrap 'rm -f \"$tmp\"' 0\ntrap 'exit 130' 1 2 15\n" +
            "hash_file() { sha256sum \"$1\" | cut -d ' ' -f 1; }\n" +
            "existed=0\nif test -e \"$dst\"; then\n" +
            "  existed=1\n  oldhash=$(hash_file \"$dst\")\n  metadata=$(stat -c '%a:%u:%g' \"$dst\")\n" +
            "  cp -p \"$dst\" \"$bak\"\n  test \"$(hash_file \"$bak\")\" = \"$oldhash\"\n  cp -p \"$dst\" \"$tmp\"\n" +
            "  context=$(stat -c '%C' \"$dst\" 2>/dev/null || true)\n" +
            "  case \"$context\" in u:*)\n    for f in \"$tmp\" \"$bak\"; do\n" +
            "      test \"$(stat -c '%C' \"$f\")\" = \"$context\" || chcon \"$context\" \"$f\"\n" +
            "      test \"$(stat -c '%C' \"$f\")\" = \"$context\"\n    done;;\n" +
            "    *) if test -r /sys/fs/selinux/enforce; then echo 'No se pudo verificar el contexto SELinux' >&2; exit 1; fi;;\n  esac\n" +
            "else\n  (set -C; : > \"$tmp\")\nfi\n" +
            "cat \"$src\" > \"$tmp\"\ntest \"$(hash_file \"$tmp\")\" = \"$expected\"\n" +
            "if test \"$existed\" = 1; then\n" +
            "  test \"$(stat -c '%a:%u:%g' \"$tmp\")\" = \"$metadata\"\n" +
            "  test \"$(stat -c '%a:%u:%g' \"$bak\")\" = \"$metadata\"\n" +
            "  test \"$(hash_file \"$dst\")\" = \"$oldhash\"\n" +
            "else\n  test ! -e \"$dst\"\nfi\ntest ! -L \"$dst\"\n" +
            "mv -f \"$tmp\" \"$dst\"\ntest \"$(hash_file \"$dst\")\" = \"$expected\"\n" +
            "if test \"$existed\" = 1; then printf '%s' \"$bak\"; fi\n";
        return new(script, backup, staging);
    }
}
