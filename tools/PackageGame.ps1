[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EngineRoot,
    [ValidateSet('Win64','Mac','Android','IOS')][string]$Platform = 'Win64',
    [ValidateSet('Development','Shipping','Debug','DebugGame','Test')][string]$Configuration = 'Development',
    [ValidateSet('x64','arm64','IOSSimulator')][string]$Architecture,
    [ValidateSet('Game','Client','Server')][string]$TargetType = 'Game',
    [string]$ArchiveDirectory,
    [switch]$SkipCook
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (Get-Process UnrealEditor* -ErrorAction SilentlyContinue) { throw 'Close the editor before packaging shared managed outputs.' }
$engine = if (Test-Path -LiteralPath (Join-Path $EngineRoot 'Engine/Build')) { Join-Path $EngineRoot 'Engine' } else { $EngineRoot }
$windowsHost = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)
$build = Join-Path $engine $(if ($windowsHost) { 'Build/BatchFiles/Build.bat' } else { 'Build/BatchFiles/Mac/Build.sh' })
$uat = Join-Path $engine $(if ($windowsHost) { 'Build/BatchFiles/RunUAT.bat' } else { 'Build/BatchFiles/RunUAT.sh' })
$hostPlatform = if ($windowsHost) { 'Win64' } else { 'Mac' }
$project = Join-Path $projectRoot 'SharpDemo.uproject'
$target = if ($TargetType -eq 'Game') { 'SharpDemo' } else { 'SharpDemo' + $TargetType }
if (!(Test-Path -LiteralPath (Join-Path $projectRoot "Source/$target.Target.cs"))) {
    throw "This project has no $TargetType native target ($target.Target.cs). Add that target before packaging it."
}
function CheckExit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed ($LASTEXITCODE)." } }
& $build $target $hostPlatform $Configuration "-Project=$project" -WaitMutex
CheckExit 'Game glue build'
$publish = @('run','--project',(Join-Path $projectRoot 'tools/ManagedModules/ModulePublisher.csproj'),'--',$Platform,'all','--publish-host','--engine-root',$EngineRoot,'--configuration',$Configuration,'--target-type',$TargetType)
if ($Architecture) { $publish += @('--architecture',$Architecture) }
& dotnet @publish
CheckExit 'Ordinary managed solution publication'
& $build SharpDemoEditor $hostPlatform Development "-Project=$project" -WaitMutex
CheckExit 'Cook editor prerequisite'
$flat = Join-Path $projectRoot 'Binaries/Managed/net10.0'
# An older desktop staging rule copied the Game installed marker into the
# development folder. Editor builds must use their own generated glue/host.
$installed = Join-Path $flat 'UnrealSharpBuild.flag'
if (Test-Path -LiteralPath $installed) { Remove-Item -LiteralPath $installed }
& $uat BuildUserSolution "-Project=$project" "-ScriptDir=$(Join-Path $projectRoot 'Plugins/UnrealSharp/Build/Scripts')" "-OutputPath=$flat" -TargetConfiguration=Development
CheckExit 'Editor managed host prerequisite'
if (!$ArchiveDirectory) { $ArchiveDirectory = Join-Path $projectRoot ("ArchivedBuilds/" + $Platform + '-' + $Configuration) }
$package = @('BuildCookRun',"-project=$project",'-Build',"-Target=$target","-TargetPlatform=$Platform","-ClientConfig=$Configuration",'-Pak','-Stage','-Archive','-package','-nocompileuat',"-archivedirectory=$ArchiveDirectory")
if ($SkipCook) { $package += '-skipcook' } else { $package += '-Cook' }
if ($Architecture) { $package += "-architecture=$Architecture" }
& $uat @package
CheckExit 'UE package'
