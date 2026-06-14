## Context

UnrealSharp projects have two solution ecosystems:
1. `SharpDemo.sln` — UE-generated, contains C++ game code (SharpDemo, UE engine modules)
2. `Script/ManagedSharpDemo.sln` — contains managed C# projects (ManagedSharpDemo, SharpDemo.Glue, SharpDemo.RuntimeGlue)

Developers must open two IDE windows to work on both. The merge tool produces a single `.sln` that can contain all projects.

## Goals / Non-Goals

**Goals:**
- Read UE `.sln` and all C# `.sln` files under `Script/`
- Merge all unique projects into a single output `.mixed.sln`
- Preserve build configurations, solution folders, and project references
- Output to same directory as UE `.sln` with name `{ProjectName}.mixed.sln`

**Non-Goals:**
- Modifying project files (`.csproj`, `.vcxproj`) — only merge `.sln` container
- Build orchestration — the merged solution is for IDE navigation only
- NuGet package publishing — simple console tool in `tools/`

## Decisions

**Decision 1: Use `Microsoft.Build` (MSBuild) APIs for SLN parsing**
- C# built-in: `SolutionFile.Parse()` for reading, `SolutionFile` + `SolutionProject` types
- Alternative: Regex/manual parsing — fragile, doesn't handle all SLN format versions
- Rationale: MSBuild ships with .NET SDK, zero extra dependencies

**Decision 2: Use `Microsoft.Build` for SLN generation**
- `SolutionFile.FromSolutionProjects()` writes valid `.sln` files
- Alternative: Custom text writer — risky, SLN format has subtle rules (GUIDs, config mappings)

**Decision 3: Project deduplication by project GUID**
- UE `.sln` and Script `.sln` may both reference same projects (e.g., UnrealSharp plugin C# glue)
- Deduplicate by `ProjectGuid` — first occurrence wins (UE solution takes priority)
- Alternative: By project path — less reliable if same project referenced from different relative paths

**Decision 4: Solution folders**
- UE projects go under `Engine/` and `Games/` folders (preserve existing structure)
- Script projects go under `Script/` folder
- Alternative: Flat structure — too many projects, hard to navigate

**Decision 5: CLI args**
```
MergeScriptSln.exe <UEProject.sln> [--output <path>]
```
- Default output: `<UEProjectDir>/<ProjectName>.mixed.sln`
- Script `.sln` auto-detected from `<UEProjectDir>/Script/*.sln`

## Risks / Trade-offs

- [Risk] MSBuild SLN API changes in newer .NET versions → Mitigation: target `net10.0`, pin `Microsoft.Build` version
- [Risk] Large solutions (UE + all Script projects) may be slow to load in IDE → Mitigation: first version is simple; solution filters can be added later
- [Risk] Solution GUID conflicts → Mitigation: project GUID deduplication, solution GUID regenerated
