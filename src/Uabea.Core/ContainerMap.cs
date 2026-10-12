// Adapted from UABEA/UABEAvalonia/Workspace/UnityContainer.cs.
// Copyright (c) 2021 nesrak1. MIT; see licenses/UABEA.txt.
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Uabea.Core;

internal static class ContainerMap
{
    public static Dictionary<long, string> Read(AssetsFileInstance file,
        Func<AssetFileInfo, AssetTypeValueField> read, List<string> logs)
    {
        var map = new Dictionary<long, string>();
        foreach (var info in file.file.Metadata.AssetInfos.Where(i => i.TypeId is 142 or 147))
        {
            try
            {
                var field = read(info)["m_Container.Array"];
                if (field.IsDummy) continue;
                foreach (var item in field.Children)
                {
                    string name = item["first"].AsString;
                    var pointer = info.TypeId == 142 ? item["second"]["asset"] : item["second"];
                    if (pointer["m_FileID"].AsInt == 0) map[pointer["m_PathID"].AsLong] = name;
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException and not OperationCanceledException)
            {
                string message = "Container no disponible: " + e.Message;
                if (!logs.Contains(message)) logs.Add(message);
            }
        }
        return map;
    }
}
