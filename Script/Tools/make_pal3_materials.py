# Creates the PAL3 material family:
#   M_Pal3Tex        - unlit opaque (emissive = tex*tint*shadow)
#   M_Pal3Tex_Add    - unlit additive (light halos / additive effects)
#   M_Pal3Tex_Alpha  - unlit translucent (alpha blended overlays / water)
# All share the same parameter names (Pal3Tex, ShadowTex, TintColor, HasShadowTex,
# ShadowExposure) so the managed material stub can rebind without caring which one.
import unreal

MAT_DIR = "/Game/PAL3"
mel = unreal.MaterialEditingLibrary
tools = unreal.AssetToolsHelpers.get_asset_tools()


def add_local_light(mat, surface, tex):
    def node(name, x, y):
        return mel.create_material_expression(mat, getattr(unreal, "MaterialExpression" + name), x, y)

    def link(source, target, slot, output=""):
        if not mel.connect_material_expressions(source, output, target, slot):
            raise RuntimeError(f"Cannot connect {source.get_name()} to {target.get_name()}.{slot}")

    lamp = node("VectorParameter", -890, -1120)
    lamp.set_editor_property("parameter_name", "LocalLight")
    lamp.set_editor_property("default_value", unreal.LinearColor(0, 0, 0, 0))
    world = node("WorldPosition", -890, -1290)
    distance = node("Distance", -645, -1290)
    link(world, distance, "A")
    link(lamp, distance, "B", "RGB")
    range_safe = node("Max", -645, -1120)
    range_safe.set_editor_property("const_b", 1.0)
    link(lamp, range_safe, "A", "A")
    distance_safe = node("Max", -465, -1290)
    distance_safe.set_editor_property("const_b", 1.0)
    link(distance, distance_safe, "A")
    fraction = node("Divide", -290, -1250)
    link(distance_safe, fraction, "A")
    link(range_safe, fraction, "B")
    inverse = node("OneMinus", -105, -1250)
    link(fraction, inverse, "")
    falloff = node("Saturate", 60, -1250)
    link(inverse, falloff, "")
    squared = node("Multiply", 220, -1250)
    link(falloff, squared, "A")
    link(falloff, squared, "B")
    warm = node("Constant3Vector", 200, -1080)
    warm.set_editor_property("constant", unreal.LinearColor(0.22, 0.091, 0.025, 1))
    light = node("Multiply", 420, -1250)
    link(squared, light, "A")
    link(warm, light, "B")
    colored = node("Multiply", 610, -1120)
    link(tex, colored, "A", "RGB")
    link(light, colored, "B")
    total = node("Add", 840, -800)
    link(surface, total, "A")
    link(colored, total, "B")
    mel.connect_material_property(total, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)


def build(name, blend):
    path = MAT_DIR + "/" + name
    if unreal.EditorAssetLibrary.does_asset_exist(path):
        unreal.EditorAssetLibrary.delete_asset(path)

    mat = tools.create_asset(name, MAT_DIR, unreal.Material, None)
    if mat is None:
        raise RuntimeError("create_asset failed: " + name)
    mat.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    mat.set_editor_property("blend_mode", blend)
    if blend != unreal.BlendMode.BLEND_OPAQUE:
        mat.set_editor_property("two_sided", True)

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
    if blend != unreal.BlendMode.BLEND_ADDITIVE:
        add_local_light(mat, final, tex)

    if blend == unreal.BlendMode.BLEND_MASKED:
        mat.set_editor_property("opacity_mask_clip_value", 0.3)
        opacity = expr(unreal.MaterialExpressionMultiply, -250, 250)
        mel.connect_material_expressions(tex, "A", opacity, "A")
        mel.connect_material_expressions(tint, "A", opacity, "B")
        mel.connect_material_property(opacity, "", unreal.MaterialProperty.MP_OPACITY_MASK)
    elif blend != unreal.BlendMode.BLEND_OPAQUE:
        # Opacity = texture alpha * tint alpha
        opacity = expr(unreal.MaterialExpressionMultiply, -250, 250)
        mel.connect_material_expressions(tex, "A", opacity, "A")
        mel.connect_material_expressions(tint, "A", opacity, "B")
        mel.connect_material_property(opacity, "", unreal.MaterialProperty.MP_OPACITY)

    mel.recompile_material(mat)
    if not unreal.EditorAssetLibrary.save_asset(path):
        raise RuntimeError("save_asset failed: " + name)
    unreal.log("[Pal3Mat] created " + name)


if __name__ == "__main__":
    build("M_Pal3Tex", unreal.BlendMode.BLEND_OPAQUE)
    build("M_Pal3Tex_Add", unreal.BlendMode.BLEND_ADDITIVE)
    build("M_Pal3Tex_Alpha", unreal.BlendMode.BLEND_TRANSLUCENT)
    build("M_Pal3Tex_Masked", unreal.BlendMode.BLEND_MASKED)
