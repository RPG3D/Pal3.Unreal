# LV_Pal3 post-process + lighting cleanup: the PAL3 unlit look must not be
# re-exposed/tonemapped by UE's automatic exposure, and the "real-time sky light"
# warning must go (PAL3 scenes have SkyBox=0).
import unreal

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
les.load_level("/Game/Maps/LV_Pal3.LV_Pal3")

actors = eas.get_all_level_actors()

# 1. Remove the remaining atmospheric actors (fog/sky atmosphere were removed before).
for a in list(actors):
    cls = a.get_class().get_name()
    if cls in ("SkyLight", "SkyAtmosphere", "ExponentialHeightFog"):
        unreal.log("[Pal3Map] destroying " + cls)
        eas.destroy_actor(a)

# 2. Unbound post-process volume: fixed exposure, no bloom/AO/motion blur.
ppv = None
for a in eas.get_all_level_actors():
    if a.get_class().get_name() == "PostProcessVolume":
        ppv = a
        break

if ppv is None:
    ppv = eas.spawn_actor_from_class(unreal.PostProcessVolume, unreal.Vector(0, 0, 0))
    unreal.log("[Pal3Map] spawned PostProcessVolume")

ppv.set_editor_property("unbound", True)
s = ppv.get_editor_property("settings")
s.set_editor_property("override_auto_exposure_method", True)
s.set_editor_property("auto_exposure_method", unreal.AutoExposureMethod.AEM_MANUAL)
s.set_editor_property("override_auto_exposure_bias", True)
s.set_editor_property("auto_exposure_bias", 0.0)
s.set_editor_property("override_auto_exposure_min_brightness", True)
s.set_editor_property("auto_exposure_min_brightness", 1.0)
s.set_editor_property("override_auto_exposure_max_brightness", True)
s.set_editor_property("auto_exposure_max_brightness", 1.0)
s.set_editor_property("override_bloom_intensity", True)
s.set_editor_property("bloom_intensity", 0.0)
s.set_editor_property("override_motion_blur_amount", True)
s.set_editor_property("motion_blur_amount", 0.0)
s.set_editor_property("override_ambient_occlusion_intensity", True)
s.set_editor_property("ambient_occlusion_intensity", 0.0)
ppv.set_editor_property("settings", s)

les.save_current_level()
unreal.log("[Pal3Map] post process configured (manual exposure)")
