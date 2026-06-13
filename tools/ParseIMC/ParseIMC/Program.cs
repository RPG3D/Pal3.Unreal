using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

IMCParser.Run();

class IMCParser
{
    public static void Run()
    {
        string assetPath = @"E:\UEProject\SharpDemo\Content\Input\IMC_Default.uasset";
        UAsset asset = new UAsset(assetPath);

        foreach (var export in asset.Exports)
        {
            Console.WriteLine($"=== [{asset.Exports.IndexOf(export)}] {export.ObjectName} ===");
            if (export is NormalExport normal)
            {
                foreach (var prop in normal.Data)
                    PrintProp(prop, 0);
            }
        }
    }

    static void PrintProp(PropertyData prop, int indent)
    {
        string pad = new string(' ', indent * 2);
        switch (prop)
        {
            case ArrayPropertyData arr:
                Console.WriteLine($"{pad}{prop.Name} [{arr.Value.Length} items]");
                foreach (var item in arr.Value)
                {
                    if (item is StructPropertyData stItem)
                    {
                        Console.WriteLine($"{pad}  ---");
                        foreach (var sp in stItem.Value)
                            PrintProp(sp, indent + 2);
                    }
                    else PrintProp(item, indent + 1);
                }
                break;
            case StructPropertyData st:
                Console.WriteLine($"{pad}{prop.Name}:");
                foreach (var sp in st.Value)
                    PrintProp(sp, indent + 1);
                break;
            case ObjectPropertyData obj:
                Console.WriteLine($"{pad}{prop.Name} = {obj.Value}");
                break;
            case NamePropertyData name:
                Console.WriteLine($"{pad}{prop.Name} = {name.Value}");
                break;
            case EnumPropertyData enm:
                Console.WriteLine($"{pad}{prop.Name} = {enm.Value}");
                break;
            case BytePropertyData bt:
                Console.WriteLine($"{pad}{prop.Name} = {bt.Value}");
                break;
            case BoolPropertyData bl:
                Console.WriteLine($"{pad}{prop.Name} = {bl.Value}");
                break;
            case FloatPropertyData flt:
                Console.WriteLine($"{pad}{prop.Name} = {flt.Value}");
                break;
            case IntPropertyData it:
                Console.WriteLine($"{pad}{prop.Name} = {it.Value}");
                break;
            default:
                Console.WriteLine($"{pad}{prop.Name} ({prop.PropertyType})");
                break;
        }
    }
}
