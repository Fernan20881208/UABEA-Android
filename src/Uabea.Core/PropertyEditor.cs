using AssetsTools.NET;
using System.Globalization;
using System.Text.Json;

namespace Uabea.Core;

public sealed record PropertyValue(string Path, string Name, string Type, string Value);

public static class PropertyEditor
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static IReadOnlyList<PropertyValue> Flatten(AssetTypeValueField root, int max = 10_000)
    {
        var result = new List<PropertyValue>();
        void Visit(AssetTypeValueField field, string path, string label, int depth)
        {
            if (result.Count >= max || depth > 128) return;
            var type = field.TemplateField.ValueType;
            if (type is not (AssetValueType.None or AssetValueType.Array or AssetValueType.ByteArray or AssetValueType.ManagedReferencesRegistry))
                result.Add(new(path, label, type.ToString(), type == AssetValueType.String ? field.AsString : Convert.ToString(field.AsObject, Invariant) ?? ""));
            for (int i = 0; i < field.Children.Count; i++) Visit(field.Children[i], path + "/" + i,
                label + "." + (field.TemplateField.IsArray ? $"[{i}]" : field.Children[i].FieldName), depth + 1);
        }
        Visit(root, "", root.FieldName, 0);
        return result;
    }

    public static string ExportJson(AssetTypeValueField root) => JsonSerializer.Serialize(Flatten(root), new JsonSerializerOptions { WriteIndented = true });

    public static void ApplyJson(AssetTypeValueField root, string json)
    {
        var edits = JsonSerializer.Deserialize<List<PropertyValue>>(json) ?? throw new FormatException("JSON vacío.");
        var paths = new HashSet<string>();
        foreach (var edit in edits)
        {
            if (!paths.Add(edit.Path)) throw new FormatException("Ruta duplicada.");
            var field = root;
            foreach (var index in edit.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                field = field.Children[int.Parse(index, Invariant)];
            if (field.TemplateField.ValueType.ToString() != edit.Type) throw new FormatException("No se puede cambiar el tipo serializado.");
            Set(field, edit.Value);
        }
    }

    public static void Set(AssetTypeValueField f, string value)
    {
        switch (f.TemplateField.ValueType)
        {
            case AssetValueType.String: f.AsString = value; break;
            case AssetValueType.Bool: f.AsBool = bool.Parse(value); break;
            case AssetValueType.Int8: f.AsSByte = sbyte.Parse(value, Invariant); break;
            case AssetValueType.UInt8: f.AsByte = byte.Parse(value, Invariant); break;
            case AssetValueType.Int16: f.AsShort = short.Parse(value, Invariant); break;
            case AssetValueType.UInt16: f.AsUShort = ushort.Parse(value, Invariant); break;
            case AssetValueType.Int32: f.AsInt = int.Parse(value, Invariant); break;
            case AssetValueType.UInt32: f.AsUInt = uint.Parse(value, Invariant); break;
            case AssetValueType.Int64: f.AsLong = long.Parse(value, Invariant); break;
            case AssetValueType.UInt64: f.AsULong = ulong.Parse(value, Invariant); break;
            case AssetValueType.Float:
                float number = float.Parse(value, Invariant);
                if (!float.IsFinite(number)) throw new FormatException("El valor debe ser finito."); f.AsFloat = number; break;
            case AssetValueType.Double:
                double real = double.Parse(value, Invariant);
                if (!double.IsFinite(real)) throw new FormatException("El valor debe ser finito."); f.AsDouble = real; break;
            default: throw new FormatException("Este campo no admite edición escalar.");
        }
    }
}
