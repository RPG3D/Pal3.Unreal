<# Builds both ordinary game projects; the launch menu selects the session. #>
param([ValidateSet('PAL3','PAL3A')][string]$Variant='PAL3A', [string]$EngineRoot=$env:UE_ENGINE_ROOT)
$ErrorActionPreference='Stop'
if (!$EngineRoot) { throw 'Set UE_ENGINE_ROOT or pass -EngineRoot.' }
$repoRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
& dotnet run --project (Join-Path $repoRoot 'tools/ManagedModules/ModulePublisher.csproj') -- Editor all --engine-root $EngineRoot
if ($LASTEXITCODE -ne 0) { throw 'Managed solution build failed.' }
Write-Host "Both code variants are built. Select $Variant from the game menu."
