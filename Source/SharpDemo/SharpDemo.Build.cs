// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;

public class SharpDemo : ModuleRules
{
	public SharpDemo(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "InputCore", "EnhancedInput", "Slate", "SlateCore" });

		// dr_libs ships FMP3Decoder (engine third-party); PAL3 music is MPEG audio
		// inside music.cpk and needs runtime decoding (no UAsset import path).
		PrivateDependencyModuleNames.AddRange(new string[] { "AudioExtensions", "dr_libs", "ProceduralMeshComponent", "RenderCore" });

		if (Target.Platform == UnrealTargetPlatform.Android)
		{
			PrivateDependencyModuleNames.AddRange(new string[] { "ApplicationCore", "Launch" });
			AdditionalPropertiesForReceipt.Add("AndroidPlugin",
				System.IO.Path.Combine(ModuleDirectory, "ProjectStorage_APL.xml"));
		}

    }
}
