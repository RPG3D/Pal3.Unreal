[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EditorExecutable,
    [ValidateSet('pal3','pal3a')][string]$GameId='pal3',
    [string]$DataPath,
    [switch]$CheckHotReload
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$arguments=@((Join-Path $root 'tools/check-editor-game-host.py'),'--editor',$EditorExecutable,'--game',$GameId)
if ($DataPath) { $arguments+=@('--data-path',$DataPath) }
if ($CheckHotReload) { $arguments+='--check-hot-reload' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw "Editor regression failed: $GameId" }
