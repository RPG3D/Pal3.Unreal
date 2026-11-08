# PAL3 host map. Idempotent: creates LV_Pal3 when missing, otherwise opens it, then
# ensures lighting and a PlayerStart exist. Referenced from DefaultEngine.ini as
# GameDefaultMap/EditorStartupMap.
import unreal

MAP_PATH = "/Game/Maps/LV_Pal3"
LEVEL_PATH = MAP_PATH + ".LV_Pal3"

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)

if unreal.EditorAssetLibrary.does_asset_exist(MAP_PATH):
    les.load_level(LEVEL_PATH)
    unreal.log("[Pal3Map] opened existing " + MAP_PATH)
else:
    if not les.new_level(MAP_PATH):
        raise RuntimeError("new_level failed")
    unreal.log("[Pal3Map] created " + MAP_PATH)

actors = eas.get_all_level_actors()

def has(cls_name):
    for a in actors:
        if a.get_class().get_name() == cls_name:
            return True
    return False

if not has("DirectionalLight"):
    sun = eas.spawn_actor_from_class(unreal.DirectionalLight, unreal.Vector(0, 0, 2000), unreal.Rotator(0, -50, 30))
    comp = sun.get_component_by_class(unreal.DirectionalLightComponent)
    if comp:
        comp.set_editor_property("intensity", 8.0)
        comp.set_editor_property("atmosphere_sun_light", True)

if not has("SkyLight"):
    sky = eas.spawn_actor_from_class(unreal.SkyLight, unreal.Vector(0, 0, 500))
    comp = sky.get_component_by_class(unreal.SkyLightComponent)
    if comp:
        comp.set_editor_property("real_time_capture", True)

if not has("SkyAtmosphere"):
    eas.spawn_actor_from_class(unreal.SkyAtmosphere, unreal.Vector(0, 0, 0))

if not has("ExponentialHeightFog"):
    eas.spawn_actor_from_class(unreal.ExponentialHeightFog, unreal.Vector(0, 0, 0))

# Without a PlayerStart the GameMode cannot spawn a pawn, so there is no camera at all.
if not has("PlayerStart"):
    eas.spawn_actor_from_class(unreal.PlayerStart, unreal.Vector(0, 0, 250), unreal.Rotator(0, 0, 0))
    unreal.log("[Pal3Map] PlayerStart added")

les.save_current_level()
unreal.log("[Pal3Map] saved " + MAP_PATH)
