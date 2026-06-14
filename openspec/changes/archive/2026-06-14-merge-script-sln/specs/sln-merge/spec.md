## ADDED Requirements

### Requirement: Merge multiple SLN files
The tool SHALL read a UE-generated `.sln` file and all `.sln` files under the `Script/` directory, merging all unique projects into a single output `.sln` file.

#### Scenario: Basic merge
- **WHEN** MergeScriptSln is run against `SharpDemo.sln` with `Script/ManagedSharpDemo.sln` present
- **THEN** output `SharpDemo.mixed.sln` contains all C++ projects from `SharpDemo.sln` and all C# projects from `Script/ManagedSharpDemo.sln`

### Requirement: Project deduplication
The tool SHALL deduplicate projects by Project GUID. When the same project appears in multiple input solutions, the first occurrence wins.

#### Scenario: Duplicate project across solutions
- **WHEN** `SharpDemo.Glue` project exists in both UE solution and Script solution with the same GUID
- **THEN** only one instance appears in the output

### Requirement: Solution folder organization
The tool SHALL preserve UE solution folder structure and place Script projects under a `Script/` solution folder.

#### Scenario: Folder structure
- **WHEN** merge completes
- **THEN** UE projects retain their original solution folder hierarchy, and Script projects are grouped under `Script/`

### Requirement: CLI interface
The tool SHALL accept the UE solution path as a positional argument and an optional `--output` flag for custom output path.

#### Scenario: Default output path
- **WHEN** run as `MergeScriptSln.exe SharpDemo.sln`
- **THEN** output is written to `SharpDemo.mixed.sln` alongside the input solution

#### Scenario: Custom output path
- **WHEN** run as `MergeScriptSln.exe SharpDemo.sln --output custom.sln`
- **THEN** output is written to `custom.sln`
