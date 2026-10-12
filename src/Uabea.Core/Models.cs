namespace Uabea.Core;

public sealed record UnityObject(string File, long PathId, int ClassId, string Type, string Name,
    long Size, string Container, string UnityVersion, bool Modified)
{
    public string Key => File + ":" + PathId;
}
public sealed record BundleEntry(string Name, long Size, bool Serialized, bool Modified);
public sealed record Change(string Key, string Description, long BeforeSize, long AfterSize,
    string BeforeSha256, string AfterSha256);
public sealed record SaveResult(string Path, string Sha256, int ObjectsVerified, int EntriesVerified);
public enum BundleCompression { None, Lz4, Lzma }
public sealed class UnityFormatException(string message, Exception? inner = null) : IOException(message, inner);

public sealed record DocumentLimits(long MaxFileBytes = 2L * 1024 * 1024 * 1024,
    long MaxExpandedBytes = 4L * 1024 * 1024 * 1024, int MaxObjectBytes = 64 * 1024 * 1024,
    int MaxObjects = 500_000, int MaxMetadataBytes = 64 * 1024 * 1024);
