using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Uabea.Core;
using Uabea.Formats;
using Uabea.Testing;
using System.Text;
using System.Text.Json;
using Uri = Android.Net.Uri;

namespace Uabea.Android;

/// <summary>Real ContentResolver/DocumentProvider I/O. Picker UI is a separate manual/emulator check.</summary>
public static class AndroidSelfTest
{
    public static async Task<string> Run(Context context, string work, string db, CancellationToken cancel)
    {
#if DEBUG
        string reportPath = Path.Combine(context.FilesDir!.AbsolutePath, "selftest.json");
        void Step(string value)
        {
            global::Android.Util.Log.Info("UabeaSelfTest", value);
            File.WriteAllText(Path.Combine(context.FilesDir.AbsolutePath, "selftest-progress.txt"), value);
        }
        try
        {
            Step("Generating the complete Unity fixture");
            string sourceDir = Path.Combine(context.FilesDir!.AbsolutePath, "selftest-documents");
            Directory.CreateDirectory(sourceDir);
            string fixture = await Task.Run(() => FixtureFactory.Create(sourceDir, db), cancel);
            Step("Creating a document through DocumentsContract");
            var source = DocumentsContract.BuildDocumentUri(TestDocumentsProvider.Authority, "fixture.bundle")!;
            var parent = DocumentsContract.BuildDocumentUri(TestDocumentsProvider.Authority, "root")!;
            var destination = DocumentsContract.CreateDocument(context.ContentResolver!, parent, "application/octet-stream", "output.bundle")!;
            var saf = new SafStorage(context, Path.Combine(work, "saf-selftest"));
            Step("Importing through ContentResolver");
            string copy = await Task.Run(() => saf.ImportAsync(source, cancel), cancel);
            string saved = Path.Combine(work, "saf-edited-" + Guid.NewGuid() + ".bundle");
            await Task.Run(() =>
            {
                Step("Opening, editing and rebuilding the Unity bundle");
                using var document = UnityDocument.Open(copy, work, db, cancel);
                var obj = document.ListObjects(cancel).Single(o => o.PathId == 1);
                TextAssetCodec.Import(document, obj, Encoding.UTF8.GetBytes("Android SAF persisted"));
                document.SaveAs(saved, BundleCompression.Lz4, cancel);
            }, cancel);
            Step("Exporting and reading back through ContentResolver");
            await Task.Run(() => saf.ExportAsync(saved, destination, source, cancel), cancel);
            string reopenedCopy = await Task.Run(() => saf.ImportAsync(destination, cancel), cancel);
            await Task.Run(() =>
            {
                using var reopened = UnityDocument.Open(reopenedCopy, work, db, cancel);
                var objects = reopened.ListObjects(cancel);
                if (objects.Count != 6 || Encoding.UTF8.GetString(TextAssetCodec.Export(reopened, objects.Single(o => o.PathId == 1))) != "Android SAF persisted")
                    throw new Exception("SAF round-trip mismatch");
                if (TextureCodec.ExportPng(reopened, objects.Single(o => o.ClassId == 28)).Length == 0) throw new Exception("PNG decode failed");
            }, cancel);
            bool blocked = false;
            Step("Validating original-document overwrite protection");
            try { await saf.ExportAsync(saved, source, source, cancel); } catch (IOException) { blocked = true; }
            if (!blocked) throw new Exception("Original URI overwrite was not blocked");
            var report = new { passed = true, sdk = (int)Build.VERSION.SdkInt, abi = Build.SupportedAbis?.FirstOrDefault(),
                tests = new[] { "ContentResolver import", "AssetsTools objects", "TextAsset edit", "LZ4 save", "ContentResolver export", "Read-back SHA256", "Reopen changed object", "Texture PNG", "Original URI overwrite rejection" },
                picker_ui_tested = false, root_tested = false };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(reportPath, json, cancel);
            global::Android.Util.Log.Info("UabeaSelfTest", "PASS " + json);
            return json;
        }
        catch (Exception e)
        {
            string json = JsonSerializer.Serialize(new { passed = false, error = e.ToString() });
            await File.WriteAllTextAsync(reportPath, json, CancellationToken.None);
            global::Android.Util.Log.Error("UabeaSelfTest", "FAIL " + e);
            throw;
        }
#else
        throw new NotSupportedException("La autoprueba SAF está incluida solo en la APK debug.");
#endif
    }
}

#if DEBUG
[ContentProvider(new[] { Authority }, Name = "com.zaid.uabea.TestDocumentsProvider", Exported = true,
    GrantUriPermissions = true, Permission = "android.permission.MANAGE_DOCUMENTS")]
public sealed class TestDocumentsProvider : DocumentsProvider
{
    public const string Authority = "com.zaid.uabea.selftest";
    static readonly string[] Columns = [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType, DocumentsContract.Document.ColumnFlags, DocumentsContract.Document.ColumnSize];
    string DirectoryPath => Path.Combine(Context!.FilesDir!.AbsolutePath, "selftest-documents");
    string PathFor(string id)
    {
        if (id == "root") return DirectoryPath;
        if (id != Path.GetFileName(id) || id.IndexOfAny(['/', '\\']) >= 0 || id is "." or "..") throw new Java.IO.FileNotFoundException();
        return Path.Combine(DirectoryPath, id);
    }
    public override bool OnCreate() => true;
    public override ICursor? QueryRoots(string[]? projection)
    {
        string[] columns = projection ?? [DocumentsContract.Root.ColumnRootId, DocumentsContract.Root.ColumnTitle, DocumentsContract.Root.ColumnDocumentId, DocumentsContract.Root.ColumnFlags];
        var cursor = new MatrixCursor(columns);
        cursor.AddRow(columns.Select(c => new Java.Lang.String(c == DocumentsContract.Root.ColumnTitle ? "UABEA self-test" : c == DocumentsContract.Root.ColumnFlags ? "0" : "root")).Cast<Java.Lang.Object>().ToArray());
        return cursor;
    }
    public override ICursor? QueryDocument(string? documentId, string[]? projection)
    {
        var cursor = new MatrixCursor(projection ?? Columns); Add(cursor, documentId!); return cursor;
    }
    public override ICursor? QueryChildDocuments(string? parentDocumentId, string[]? projection, string? sortOrder)
    {
        var cursor = new MatrixCursor(projection ?? Columns);
        foreach (var path in Directory.GetFiles(DirectoryPath)) Add(cursor, Path.GetFileName(path));
        return cursor;
    }
    void Add(MatrixCursor cursor, string id)
    {
        string path = PathFor(id); bool root = id == "root";
        var values = cursor.GetColumnNames()!.Select(column => new Java.Lang.String(column switch
        {
            DocumentsContract.Document.ColumnDocumentId => id,
            DocumentsContract.Document.ColumnDisplayName => id,
            DocumentsContract.Document.ColumnMimeType => root ? DocumentsContract.Document.MimeTypeDir : "application/octet-stream",
            DocumentsContract.Document.ColumnFlags => root ? "8" : "6",
            DocumentsContract.Document.ColumnSize => root ? "0" : new FileInfo(path).Length.ToString(),
            _ => ""
        })).Cast<Java.Lang.Object>().ToArray(); cursor.AddRow(values);
    }
    public override ParcelFileDescriptor? OpenDocument(string? documentId, string? mode, CancellationSignal? signal)
        => ParcelFileDescriptor.Open(new Java.IO.File(PathFor(documentId!)), ParcelFileDescriptor.ParseMode(mode!));
    public override string? CreateDocument(string? parentDocumentId, string? mimeType, string? displayName)
    {
        if (parentDocumentId != "root") throw new Java.IO.FileNotFoundException();
        Directory.CreateDirectory(DirectoryPath);
        string name = Guid.NewGuid().ToString("N") + "-" + Uabea.Core.StreamTools.SafeName(displayName ?? "output.bundle");
        File.WriteAllBytes(PathFor(name), []); return name;
    }
    public override void DeleteDocument(string? documentId)
    {
        if (documentId == "root") throw new Java.IO.FileNotFoundException();
        File.Delete(PathFor(documentId!));
    }
}
#endif
