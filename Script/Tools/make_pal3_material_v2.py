# Rebuild /Game/PAL3/M_Pal3Tex as the UE equivalent of the Unity "Pal3/Opaque" shader
# (unlit, texture * tint * shadow-lightmap), and strip the sky/fog actors from LV_Pal3
# so the room renders on a black background like the original.
# Run: UnrealEditor-Cmd.exe <uproject> -run=pythonscript -script=<this>
import unreal

MAT_DIR = "/Game/PAL3"
MAT_NAME = "M_Pal3Tex"
MAT_PATH = MAT_DIR + "/" + MAT_NAME

if unreal.EditorAssetLibrary.does_asset_exist(MAT_PATH):
    unreal.EditorAssetLibrary.delete_asset(MAT_PATH)
    unreal.log("[Pal3Mat] deleted old " + MAT_PATH)

if not unreal.EditorAssetLibrary.does_directory_exist(MAT_DIR):
    unreal.EditorAssetLibrary.make_directory(MAT_DIR)

tools = unreal.AssetToolsHelpers.get_asset_tools()
mat = tools.create_asset(MAT_NAME, MAT_DIR, unreal.Material, None)
if mat is None:
    raise RuntimeError("create_asset(M_Pal3Tex) failed")

mat.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
mat.set_editor_property("blend_mode", unreal.BlendMode.BLEND_OPAQUE)

mel = unreal.MaterialEditingLibrary

def expr(cls, x, y):
    return mel.create_material_expression(mat, cls, x, y)

tex = expr(unreal.MaterialExpressionTextureSampleParameter2D, -950, -350)
tex.set_editor_property("parameter_name", "Pal3Tex")

tint = expr(unreal.MaterialExpressionVectorParameter, -950, -550)
tint.set_editor_property("parameter_name", "TintColor")
tint.set_editor_property("default_value", unreal.LinearColor(1.0, 1.0, 1.0, 1.0))

shadow = expr(unreal.MaterialExpressionTextureSampleParameter2D, -950, 50)
shadow.set_editor_property("parameter_name", "ShadowTex")

uv1 = expr(unreal.MaterialExpressionTextureCoordinate, -1150, 50)
uv1.set_editor_property("coordinate_index", 1)
mel.connect_material_expressions(uv1, "", shadow, "UVs")

has_shadow = expr(unreal.MaterialExpressionScalarParameter, -950, 350)
has_shadow.set_editor_property("parameter_name", "HasShadowTex")
has_shadow.set_editor_property("default_value", 0.0)

exposure = expr(unreal.MaterialExpressionScalarParameter, -1150, 500)
exposure.set_editor_property("parameter_name", "ShadowExposure")
exposure.set_editor_property("default_value", 0.4)

one = expr(unreal.MaterialExpressionConstant, -1150, 650)
one.set_editor_property("r", 1.0)

one_minus = expr(unreal.MaterialExpressionSubtract, -950, 600)
mel.connect_material_expressions(one, "", one_minus, "A")
mel.connect_material_expressions(exposure, "", one_minus, "B")

shadow_scaled = expr(unreal.MaterialExpressionDivide, -720, 300)
mel.connect_material_expressions(shadow, "RGB", shadow_scaled, "A")
mel.connect_material_expressions(one_minus, "", shadow_scaled, "B")

shadow_factor = expr(unreal.MaterialExpressionLinearInterpolate, -500, 300)
mel.connect_material_expressions(one, "", shadow_factor, "A")
mel.connect_material_expressions(shadow_scaled, "", shadow_factor, "B")
mel.connect_material_expressions(has_shadow, "", shadow_factor, "Alpha")

base_tint = expr(unreal.MaterialExpressionMultiply, -700, -350)
mel.connect_material_expressions(tex, "RGB", base_tint, "A")
mel.connect_material_expressions(tint, "RGB", base_tint, "B")

final = expr(unreal.MaterialExpressionMultiply, -250, -50)
mel.connect_material_expressions(base_tint, "", final, "A")
mel.connect_material_expressions(shadow_factor, "", final, "B")
mel.connect_material_property(final, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)

mel.recompile_material(mat)
if not unreal.EditorAssetLibrary.save_asset(MAT_PATH):
    raise RuntimeError("save_asset failed")
unreal.log("[Pal3Mat] created unlit " + MAT_PATH)

# --- LV_Pal3: drop sky atmosphere + height fog (PAL3 scenes have SkyBox=0; the room
# must render on black, not under a blue sky). Lights stay for any lit fallback mat.
les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
les.load_level("/Game/PAL3/Maps/LV_Pal3.LV_Pal3")
removed = []
for a in eas.get_all_level_actors():
    cls_name = a.get_class().get_name()
    if cls_name in ("SkyAtmosphere", "ExponentialHeightFog"):
        removed.append(cls_name)
        eas.destroy_actor(a)
les.save_current_level()
unreal.log("[Pal3Map] removed sky/fog actors: " + ", ".join(removed))
