import unreal
MAT_PATH = "/Game/Pal3/M_Pal3Tex"
lines = []
if unreal.EditorAssetLibrary.does_asset_exist(MAT_PATH):
    mat = unreal.EditorAssetLibrary.load_asset(MAT_PATH)
    mel = unreal.MaterialEditingLibrary
    exprs = mel.get_material_expressions(mat)
    lines.append(f"expressions: {len(exprs)}")
    # find a colorful ENGINE texture by scanning the engine resources dir
    candidates = []
    for name in ["/Engine/EngineResources/DefaultTextureChecker", "/Engine/EngineMaterials/DefaultTextureChecker",
                 "/Engine/EngineResources/DefaultTexture", "/Engine/EngineMaterials/GridTexture"]:
        if unreal.EditorAssetLibrary.does_asset_exist(name):
            candidates.append(name)
    lines.append("candidates: " + repr(candidates))
    if candidates:
        tex = unreal.EditorAssetLibrary.load_asset(candidates[0])
        for e in exprs:
            try:
                e.set_editor_property("texture", tex)
                mel.recompile_material(mat)
                unreal.EditorAssetLibrary.save_asset(MAT_PATH)
                lines.append("default texture set to: " + candidates[0])
                break
            except Exception as ex:
                lines.append("set failed: " + str(ex))
else:
    lines.append("material missing")
with open(r'D:/UEProject/SharpDemo/probe_out.txt', 'w') as f:
    f.write(chr(10).join(lines))
