using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.JSON;
using Newtonsoft.Json;

string assetPath = @"E:\UEProject\SharpDemo\Content\Input\IMC_Default.uasset";
UAsset asset = new UAsset(assetPath);

// Dump the modifier exports specifically
for (int i = 0; i < asset.Exports.Count; i++)
{
    var export = asset.Exports[i];
    if (export is NormalExport normal)
    {
        Console.WriteLine($"=== [{i}] {export.ObjectName} ===");
        if (normal.Data != null && normal.Data.Count > 0)
        {
            foreach (var prop in normal.Data)
            {
                DumpProp(prop, 0);
            }
        }
        else
        {
            Console.WriteLine("  (empty)");
        }
    }
}

void DumpProp(PropertyData prop, int indent)
{
    string pad = new string(' ', indent * 2 + 2);
    switch (prop)
    {
        case ArrayPropertyData arr:
            Console.WriteLine($"{pad}{prop.Name} [{arr.Value.Length}]");
            foreach (var v in arr.Value) DumpProp(v, indent + 1);
            break;
        case StructPropertyData st:
            Console.WriteLine($"{pad}{prop.Name}");
            foreach (var v in st.Value) DumpProp(v, indent + 1);
            break;
        case EnumPropertyData e:
            Console.WriteLine($"{pad}{prop.Name} = {e.Value} (enum)");
            break;
        case BytePropertyData b:
            Console.WriteLine($"{pad}{prop.Name} = {b.Value}");
            break;
        case BoolPropertyData bl:
            Console.WriteLine($"{pad}{prop.Name} = {bl.Value}");
            break;
        case FloatPropertyData f:
            Console.WriteLine($"{pad}{prop.Name} = {f.Value}");
            break;
        case IntPropertyData it:
            Console.WriteLine($"{pad}{prop.Name} = {it.Value}");
            break;
        case NamePropertyData n:
            Console.WriteLine($"{pad}{prop.Name} = {n.Value}");
            break;
        case ObjectPropertyData o:
            Console.WriteLine($"{pad}{prop.Name} = {o.Value}");
            break;
        case RawStructPropertyData rs:
            Console.WriteLine($"{pad}{prop.Name} (raw struct, {rs.StructType})");
            break;
        default:
            Console.WriteLine($"{pad}{prop.Name} ({prop.PropertyType})");
            break;
    }
}
