// RuntimeWidgetLayout.Build.cs
using UnrealBuildTool;

public class RuntimeWidgetLayout : ModuleRules
{
	public RuntimeWidgetLayout(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core", "CoreUObject", "Engine", "InputCore", "UMG", "Slate", "SlateCore", "Json"
		});
	}
}
