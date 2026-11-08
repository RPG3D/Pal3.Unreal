import unreal
les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
les.load_level("/Game/PAL3/Maps/LV_Pal3.LV_Pal3")
for a in eas.get_all_level_actors():
    if a.get_class().get_name() == "PostProcessVolume":
        eas.destroy_actor(a)
        unreal.log("[Pal3Map] removed PostProcessVolume")
les.save_current_level()
