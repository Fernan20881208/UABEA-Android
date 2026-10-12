using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Uabea.Core;
using Uabea.Formats;
using Uabea.Testing;

string root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/tests");
Directory.CreateDirectory(root);
string db = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
var results = new List<(string Name, Exception? Error)>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action action)
{
    try { action(); results.Add((name, null)); Console.WriteLine("PASS " + name); }
    catch (Exception e) { results.Add((name, e)); Console.WriteLine("FAIL " + name + "\n" + e); }
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
}

string fixture = "";
Test("Generate original full UnityFS fixture", () => fixture = FixtureFactory.Create(Path.Combine(root, "fixtures"), db));
Test("Open full bundle, directory, objects, names, versions and containers", () =>
{
    using var doc = UnityDocument.Open(fixture, root, db);
    Check(doc.Entries.Count == 2, "Expected assets and resS entries");
    var objects = doc.ListObjects(); Check(objects.Count == 6, "Expected six serialized objects");
    Check(objects.Single(x => x.PathId == 1).Name == "editable-text", "Name decoding");
    Check(objects.Single(x => x.PathId == 1).Container == "assets/uabea/fixture-1", "Container map");
    Check(objects.All(x => x.UnityVersion == "2021.3.0f1"), "Version");
});
foreach (var compression in Enum.GetValues<BundleCompression>())
    Test("TextAsset edit, save, reopen and unchanged bytes: " + compression, () =>
    {
        using var doc = UnityDocument.Open(fixture, root, db);
        var objects = doc.ListObjects(); var text = objects.Single(x => x.PathId == 1);
        var other = objects.Where(x => x.PathId != 1).ToDictionary(x => x.PathId, x => StreamTools.HashBytes(doc.ReadRaw(x)));
        Check(Encoding.UTF8.GetString(TextAssetCodec.Export(doc, text)) == "Hello Android!\n", "Text export");
        byte[] changed = Encoding.UTF8.GetBytes("¡Hola Zaid! 🟣\nA longer replacement with preserved timing-free text.");
        TextAssetCodec.Import(doc, text, changed);
        string output = Path.Combine(root, "saved-" + compression + ".bundle"); File.Delete(output);
        var saved = doc.SaveAs(output, compression);
        Check(saved.ObjectsVerified == 6, "All objects must be verified");
        using var reopened = UnityDocument.Open(output, root, db);
        Check(reopened.OriginalCompression == compression, "Compression round trip");
        var after = reopened.ListObjects();
        Check(TextAssetCodec.Export(reopened, after.Single(x => x.PathId == 1)).SequenceEqual(changed), "Edited bytes persisted");
        foreach (var o in after.Where(x => x.PathId != 1)) Check(other[o.PathId] == StreamTools.HashBytes(reopened.ReadRaw(o)), "Unchanged object changed");
        using var resource = new MemoryStream(); reopened.ExtractEntry("fixture.resS", resource);
        Check(resource.ToArray().SequenceEqual(new byte[] { 10, 20, 30, 40, 50 }), "Resource changed");
    });
Test("Standalone .assets edit and save", () =>
{
    using var doc = UnityDocument.Open(Path.Combine(root, "fixtures/fixture.assets"), root, db);
    var text = doc.ListObjects().Single(x => x.PathId == 1);
    TextAssetCodec.Import(doc, text, Encoding.UTF8.GetBytes("standalone"));
    string output = Path.Combine(root, "standalone.assets"); File.Delete(output);
    doc.SaveAs(output);
    using var reopened = UnityDocument.Open(output, root, db);
    Check(Encoding.UTF8.GetString(TextAssetCodec.Export(reopened, reopened.ListObjects().Single(x => x.PathId == 1))) == "standalone", "Standalone edit");
});
Test("Material scalar/color/PPtr editing and shader inspection", () =>
{
    using var doc = UnityDocument.Open(fixture, root, db);
    var material = doc.ListObjects().Single(x => x.ClassId == 21);
    var field = doc.ReadEditableField(material);
    field["m_SavedProperties"]["m_Floats.Array"][0]["second"].AsFloat = 0.8f;
    field["m_SavedProperties"]["m_Colors.Array"][0]["second"]["g"].AsFloat = 0.5f;
    doc.ReplaceField(material, field, "material test");
    var shader = doc.ListObjects().Single(x => x.ClassId == 48);
    Check(doc.ReadField(shader)["m_Name"].AsString == "fixture-placeholder-shader", "Shader inspection");
    string output = Path.Combine(root, "material.bundle"); File.Delete(output); doc.SaveAs(output);
    using var reopened = UnityDocument.Open(output, root, db);
    field = reopened.ReadField(reopened.ListObjects().Single(x => x.ClassId == 21));
    Check(field["m_SavedProperties"]["m_Floats.Array"][0]["second"].AsFloat == 0.8f, "Float persistence");
    Check(field["m_SavedProperties"]["m_Colors.Array"][0]["second"]["g"].AsFloat == 0.5f, "Color persistence");
});
Test("PNG export, orientation, RGBA32 replacement and reopening", () =>
{
    using var doc = UnityDocument.Open(fixture, root, db);
    var tex = doc.ListObjects().Single(x => x.ClassId == 28);
    byte[] png = TextureCodec.ExportPng(doc, tex);
    var decoded = StbImageSharp.ImageResult.FromMemory(png, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
    Check(decoded.Data.AsSpan(0, 4).SequenceEqual(new byte[] { 255,0,0,255 }), "Top-left must be red, not flipped");
    TextureCodec.ImportPngRgba32(doc, tex, png);
    string output = Path.Combine(root, "texture.bundle"); File.Delete(output); doc.SaveAs(output);
    using var reopened = UnityDocument.Open(output, root, db);
    var retex = reopened.ListObjects().Single(x => x.ClassId == 28);
    Check(TextureCodec.Info(reopened, retex).Format == "RGBA32", "Format");
    Check(StbImageSharp.ImageResult.FromMemory(TextureCodec.ExportPng(reopened, retex), StbImageSharp.ColorComponents.RedGreenBlueAlpha).Data.SequenceEqual(decoded.Data), "Pixel preservation");
    File.WriteAllBytes(Path.Combine(root, "texture.png"), png);
});
Test("Entry replacement including serialized entry", () =>
{
    using var doc = UnityDocument.Open(fixture, root, db);
    string replacement = Path.Combine(root, "replacement.resS"); File.WriteAllText(replacement, "replacement resource");
    doc.ReplaceEntry("fixture.resS", replacement);
    doc.ReplaceEntry("CAB-uabea-fixture", Path.Combine(root, "fixtures/fixture.assets"));
    string output = Path.Combine(root, "entries.bundle"); File.Delete(output); doc.SaveAs(output);
    using var reopened = UnityDocument.Open(output, root, db);
    using var raw = new MemoryStream(); reopened.ExtractEntry("fixture.resS", raw);
    Check(Encoding.UTF8.GetString(raw.ToArray()) == "replacement resource", "Entry replacement");
});
Test("Property JSON typed edits and RAW validation reject incompatible data", () =>
{
    using var doc = UnityDocument.Open(fixture, root, db);
    var text = doc.ListObjects().Single(x => x.PathId == 1);
    var field = doc.ReadEditableField(text);
    var properties = PropertyEditor.Flatten(field).Select(p => p.Name.EndsWith("m_Name") ? p with { Value = "renamed" } : p).ToList();
    PropertyEditor.ApplyJson(field, JsonSerializer.Serialize(properties));
    doc.ReplaceField(text, field, "name");
    Check(doc.ListObjects().Single(x => x.PathId == 1).Name == "renamed", "Live name after replacement");
    Throws<Exception>(() => doc.ReplaceRaw(text, new byte[] { 1,2,3 }));
    Check(doc.ListObjects().Single(x => x.PathId == 1).Name == "renamed", "Failed import must preserve previous edit");
    doc.Revert(text); Check(doc.Changes.Count == 0, "Undo");
});
Test("Original never overwritten, existing destinations rejected, cancellation cleanup", () =>
{
    using var doc = UnityDocument.Open(fixture, root, db);
    string hash = StreamTools.HashFile(fixture);
    Throws<IOException>(() => doc.SaveAs(fixture));
    string output = Path.Combine(root, "existing.bundle"); File.WriteAllText(output, "keep");
    Throws<IOException>(() => doc.SaveAs(output)); Check(File.ReadAllText(output) == "keep", "Existing target preserved");
    string cancelOutput = Path.Combine(root, "cancelled.bundle"); File.Delete(cancelOutput);
    Throws<OperationCanceledException>(() => doc.SaveAs(cancelOutput, cancel: new CancellationToken(true)));
    Check(!File.Exists(cancelOutput) && !Directory.GetFiles(root, "*.partial-*").Any(), "Cancelled output cleanup");
    Check(StreamTools.HashFile(fixture) == hash, "Original preserved");
});
Test("Corrupt, truncated, encrypted-looking and unsupported headers rejected", () =>
{
    foreach (var (name, bytes) in new[] {
        ("truncated", File.ReadAllBytes(fixture)[..40]),
        ("opaque", Encoding.ASCII.GetBytes("not-a-standard-bundle-encrypted-or-proprietary")),
        ("legacy", Encoding.ASCII.GetBytes("UnityRaw\0this-version-is-not-supported-----")) })
    {
        string file = Path.Combine(root, name); File.WriteAllBytes(file, bytes);
        Throws<UnityFormatException>(() => { using var doc = UnityDocument.Open(file, root, db); });
    }
});
var suite = new XElement("testsuite", new XAttribute("name", "UABEA.Android.Core"), new XAttribute("tests", results.Count),
    new XAttribute("failures", results.Count(x => x.Error != null)), results.Select(r => new XElement("testcase", new XAttribute("name", r.Name),
        r.Error == null ? null : new XElement("failure", new XAttribute("message", r.Error.Message), r.Error.ToString()))));
new XDocument(suite).Save(Path.Combine(root, "junit.xml"));
File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { passed = results.Count(x => x.Error == null), failed = results.Count(x => x.Error != null), tests = results.Select(x => new { name = x.Name, passed = x.Error == null, error = x.Error?.ToString() }) }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count(x => x.Error == null)}/{results.Count} passed");
return results.Any(x => x.Error != null) ? 1 : 0;
