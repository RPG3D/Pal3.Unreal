## 1. Project Scaffold

- [x] 1.1 Create `tools/MergeScriptSln/MergeScriptSln.csproj` (console, net10.0)
- [x] 1.2 Add `Microsoft.Build` NuGet package reference
- [x] 1.3 Add to `tools/` directory matching existing `ParseIMC` convention

## 2. Core Merge Logic

- [x] 2.1 Parse UE `.sln` with `SolutionFile.Parse()`
- [x] 2.2 Discover all `Script/**/*.sln` files and parse them
- [x] 2.3 Collect unique projects by `ProjectGuid`, dedup preferentially from UE sln first
- [x] 2.4 Build solution folder structure: UE folders preserved, Script projects under `Script/`

## 3. Output

- [x] 3.1 Generate merged `.sln` using `SolutionFile` + project list
- [x] 3.2 Default output path: `<input-dir>/<ProjectName>.mixed.sln`
- [x] 3.3 Support `--output <path>` CLI flag for custom output

## 4. Integration

- [x] 4.1 Add merge step to `BuildManagedCode.bat` (after generated project files)
- [x] 4.2 Verify merged solution opens correctly in Rider/VS
