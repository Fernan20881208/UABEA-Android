// Adapted from UABEA/TextAssetPlugin/Program.cs, MIT (nesrak1).
using Uabea.Core;

namespace Uabea.Formats;

public static class TextAssetCodec
{
    public static byte[] Export(UnityDocument document, UnityObject obj)
    {
        if (obj.ClassId != 49) throw new ArgumentException("Selecciona un TextAsset.");
        return document.ReadField(obj)["m_Script"].AsByteArray;
    }
    public static void Import(UnityDocument document, UnityObject obj, byte[] bytes)
    {
        if (obj.ClassId != 49) throw new ArgumentException("Selecciona un TextAsset.");
        var root = document.ReadEditableField(obj);
        root["m_Script"].AsByteArray = bytes;
        document.ReplaceField(obj, root, "Texto importado (m_Script)");
    }
}
