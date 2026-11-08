# One-time editor-python: creates the PAL3 runtime parent material with a single
# texture parameter "Pal3Tex". Run via: UnrealEditor-Cmd.exe <uproject> -run=pythonscript -script=<this>
import unreal

MAT_PATH = "/Game/Pal3/M_Pal3Tex"

if unreal.EditorAssetLibrary.does_asset_exist(MAT_PATH):
    unreal.log("[Pal3Mat] material already exists: " + MAT_PATH)
else:
    unreal.EditorAssetLibrary.make_directory("/Game/Pal3")
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    mat = tools.create_asset("M_Pal3Tex", "/Game/Pal3", unreal.Material, None)
    if mat is None:
        raise RuntimeError("create_asset(M_Pal3Tex) failed")
    mel = unreal.MaterialEditingLibrary
    node = mel.create_material_expression(
        mat, unreal.MaterialExpressionTextureSampleParameter2D, -400, 0)
    node.set_editor_property("parameter_name", "Pal3Tex")
    mel.connect_material_property(node, "RGB", unreal.MaterialProperty.MP_BASE_COLOR)
    # PAL3 renders with its own baked-ish look; default lit is fine for now.
    mel.recompile_material(mat)
    if not unreal.EditorAssetLibrary.save_asset(MAT_PATH):
        raise RuntimeError("save_asset failed")
    unreal.log("[Pal3Mat] created " + MAT_PATH)
