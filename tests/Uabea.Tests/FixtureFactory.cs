// MIT. Generated fixtures contain only original synthetic data, no game assets.
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace Uabea.Testing;

public static class FixtureFactory
{
    public static string Create(string directory, string classData)
    {
        Directory.CreateDirectory(directory);
        var manager = new AssetsManager();
        using (var stream = File.OpenRead(classData)) manager.LoadClassPackage(stream);
        var database = manager.LoadClassDatabaseFromPackage("2021.3.0f1");
        var file = new AssetsFile
        {
            Header = new() { Version = 22, Endianness = false, DataOffset = 4096 },
            Metadata = new()
            {
                UnityVersion = "2021.3.0f1", TargetPlatform = 13, TypeTreeEnabled = true,
                TypeTreeTypes = [], AssetInfos = new List<AssetFileInfo>(), ScriptTypes = [], Externals = [], RefTypes = [], UserInformation = "UABEA Android synthetic MIT fixtures"
            },
            Reader = new AssetsFileReader(new MemoryStream())
        };
        AssetTypeValueField Add(int classId, long pathId, Action<AssetTypeValueField> set)
        {
            var template = new AssetTypeTemplateField();
            template.FromClassDatabase(database, database.FindAssetClassByID(classId));
            int index = file.Metadata.TypeTreeTypes.FindIndex(t => t.TypeId == classId);
            if (index == -1)
            {
                index = file.Metadata.TypeTreeTypes.Count;
                var type = TemplateFieldToTypeTree.Convert(template, classId);
                type.TypeBlobIsDefinition = true;
                file.Metadata.TypeTreeTypes.Add(type);
            }
            var root = ValueBuilder.DefaultValueFieldFromTemplate(template);
            set(root);
            var info = new AssetFileInfo { PathId = pathId, TypeId = classId, TypeIdOrIndex = index, ScriptTypeIndex = 0xffff };
            info.SetNewData(root); file.Metadata.AddAssetInfo(info);
            return root;
        }
        Add(49, 1, root => { root["m_Name"].AsString = "editable-text"; root["m_Script"].AsString = "Hello Android!\n"; });
        Add(49, 2, root => { root["m_Name"].AsString = "unchanged-text"; root["m_Script"].AsString = "Keep these exact bytes.\n"; });
        Add(21, 3, root =>
        {
            root["m_Name"].AsString = "test-material";
            root["m_Shader"]["m_FileID"].AsInt = 0; root["m_Shader"]["m_PathID"].AsLong = 5;
            var floats = root["m_SavedProperties"]["m_Floats.Array"];
            var entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(floats);
            entry["first"].AsString = "_Glossiness"; entry["second"].AsFloat = 0.25f; floats.Children.Add(entry);
            var colors = root["m_SavedProperties"]["m_Colors.Array"];
            entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(colors);
            entry["first"].AsString = "_Color"; entry["second"]["r"].AsFloat = 1; entry["second"]["a"].AsFloat = 1; colors.Children.Add(entry);
        });
        Add(28, 4, root =>
        {
            root["m_Name"].AsString = "test-texture";
            var tex = TextureFile.ReadTextureFile(root);
            tex.m_ImageCount = 1; tex.m_TextureDimension = 2;
            tex.SetPictureData(new byte[] { 0,0,255,255, 255,255,255,255, 255,0,0,255, 0,255,0,255 }, 2, 2, TextureFormat.RGBA32);
            tex.WriteTo(root);
        });
        Add(48, 5, root => { root["m_Name"].AsString = "fixture-placeholder-shader"; });
        Add(142, 6, root =>
        {
            root["m_Name"].AsString = "uabea-test";
            var array = root["m_Container.Array"];
            for (long id = 1; id <= 5; id++)
            {
                var entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
                entry["first"].AsString = "assets/uabea/fixture-" + id;
                entry["second"]["asset"]["m_FileID"].AsInt = 0;
                entry["second"]["asset"]["m_PathID"].AsLong = id;
                array.Children.Add(entry);
            }
        });
        string assets = Path.Combine(directory, "fixture.assets");
        using (var writer = new AssetsFileWriter(File.Create(assets))) file.Write(writer);
        file.Close();
        var main = AssetBundleDirectoryInfo.Create("CAB-uabea-fixture", true); main.SetNewData(File.ReadAllBytes(assets));
        var resource = AssetBundleDirectoryInfo.Create("fixture.resS", false); resource.SetNewData(new byte[] { 10, 20, 30, 40, 50 });
        var bundle = new AssetBundleFile
        {
            Header = new() { Signature = "UnityFS", Version = 6, GenerationVersion = "5.x.x", EngineVersion = "2021.3.0f1",
                FileStreamHeader = new() { Flags = AssetBundleFSHeaderFlags.HasDirectoryInfo } },
            BlockAndDirInfo = new() { Hash = new Hash128(), BlockInfos = [], DirectoryInfos = [main, resource] },
            DataReader = new AssetsFileReader(new MemoryStream())
        };
        string path = Path.Combine(directory, "fixture.bundle");
        using (var writer = new AssetsFileWriter(File.Create(path))) bundle.Write(writer);
        manager.UnloadAll(true);
        return path;
    }
}
