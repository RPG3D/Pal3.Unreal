using UnrealBuildTool;

public class RuntimeBinkVideo : ModuleRules
{
	public RuntimeBinkVideo(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "UMG", "SlateCore" });
		PrivateDependencyModuleNames.AddRange(new[] { "MediaAssets", "Slate" });
		if (Target.Platform == UnrealTargetPlatform.Android)
		{
			PrivateDependencyModuleNames.AddRange(new[] { "ApplicationCore", "Launch" });
			AdditionalPropertiesForReceipt.Add("AndroidPlugin",
				System.IO.Path.Combine(ModuleDirectory, "RuntimeBinkVideo_APL.xml"));
		}
	}
}
