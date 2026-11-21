// Copyright Epic Games, Inc. All Rights Reserved.

using System.IO;
using UnrealBuildTool;

public class SharpDemo : ModuleRules
{
	public SharpDemo(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "InputCore", "EnhancedInput", "Slate", "SlateCore" });

		// Generic managed assembly and platform-file APIs.
		PublicDependencyModuleNames.AddRange(new string[] { "UnrealSharpCore", "UnrealSharpUtilities" });

		// dr_libs ships FMP3Decoder (engine third-party); PAL3 music is MPEG audio
		// inside music.cpk and needs runtime decoding (no UAsset import path).
		PrivateDependencyModuleNames.AddRange(new string[] { "AudioExtensions", "dr_libs", "ProceduralMeshComponent", "RenderCore" });

		// Editor-only automation helpers on UPal3DebugOverlay (start/stop PIE over
		// Remote Control, and editor console exec when no game viewport exists).
		if (Target.bBuildEditor)
		{
			PrivateDependencyModuleNames.AddRange(new string[] { "UnrealEd", "LevelEditor" });
		}

		// Desktop UAT writes its standard archive layout. Stage those project
		// outputs without copying Game DLLs over the editor's development output.
		if (!Target.bBuildEditor && (Target.Platform == UnrealTargetPlatform.Win64 || Target.Platform == UnrealTargetPlatform.Mac))
		{
			string ProjectDir = Target.ProjectFile!.Directory.FullName;
			string Managed = Path.Combine(ProjectDir, "Content", "Managed", Target.Platform.ToString(), "Binaries", "Managed", "net10.0");
			ExternalDependencies.Add(Path.Combine(ProjectDir, "Intermediate", "UnrealSharp", "Publish", Target.Platform + ".stamp"));
			if (Directory.Exists(Managed))
				foreach (string File in Directory.EnumerateFiles(Managed, "*", SearchOption.AllDirectories))
					RuntimeDependencies.Add(File, StagedFileType.NonUFS);
		}


		if (Target.Platform == UnrealTargetPlatform.Android)
		{
			PrivateDependencyModuleNames.AddRange(new string[] { "ApplicationCore", "Launch" });
			AdditionalPropertiesForReceipt.Add("AndroidPlugin",
				System.IO.Path.Combine(ModuleDirectory, "ProjectStorage_APL.xml"));
		}
		else if (Target.Platform == UnrealTargetPlatform.IOS)
		{
			// The shared engine PCH is compiled without ARC.
			PCHUsage = PCHUsageMode.NoSharedPCHs;
			bEnableObjCAutomaticReferenceCounting = true;
			PrivateDependencyModuleNames.Add("ApplicationCore");
			PublicFrameworks.AddRange(new[] { "UIKit", "UniformTypeIdentifiers" });
		}

    }
}
