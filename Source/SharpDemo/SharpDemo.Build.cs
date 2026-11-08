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
		PrivateDependencyModuleNames.AddRange(new string[] { "dr_libs" });

    }
}
