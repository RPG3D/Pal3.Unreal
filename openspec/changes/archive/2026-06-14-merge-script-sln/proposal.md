## Why

The UnrealSharp plugin maintains managed C# projects in `Script/` (e.g., `ManagedSharpDemo`, `SharpDemo.Glue`, `SharpDemo.RuntimeGlue`) and their solution file `Script/ManagedSharpDemo.sln`. These live separately from the UE-generated project solution (`SharpDemo.sln`). Developers must switch between two solutions to work on C++ and C# code, which is cumbersome. A single merged solution would allow one IDE window for all code.

## What Changes

- New `tools/MergeScriptSln/` C# console tool that reads a UE `.sln` file and all C# `.sln` files under `Script/`
- Merges all projects into a single `{ProjectName}.mixed.sln` placed alongside the UE solution
- Uses Microsoft.Build (MSBuild) SLN APIs for correct `.sln` file generation
- Run as a standalone build step or manually after generating C# projects

## Capabilities

### New Capabilities
- `sln-merge`: Merge multiple .NET solution files into one, preserving all project references and build configurations

### Modified Capabilities
<!-- None -->

## Impact

- New tool in `tools/MergeScriptSln/` — no existing code modified
- `Microsoft.Build` NuGet dependency for SLN parsing/generation
- Optional integration into build pipeline (BuildManagedCode.bat)
