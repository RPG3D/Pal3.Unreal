[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ObjectPath,
    [string] $RemoteUrl = 'http://127.0.0.1:30010',
    [ValidateRange(1, 99)] [int] $Slot = 5
)

$ErrorActionPreference = 'Stop'
$client = Join-Path $PSScriptRoot 'pal3-play.ps1'
$rc = @{ ObjectPath = $ObjectPath; RemoteUrl = $RemoteUrl }

$slots = & $client slots @rc | ConvertFrom-Json
if (($slots | Where-Object slot -eq $Slot).occupied) {
    throw "Slot $Slot is occupied. Choose an empty manual save slot."
}

& $client checkpoint 1 @rc | Out-Null
$spawn = & $client wait -State Gameplay -City q01 -Scene b -TimeoutSeconds 30 @rc | ConvertFrom-Json
& $client move-to '194 212 walk' @rc | Out-Null
$saved = & $client wait -State Gameplay -TileX 194 -TileY 212 -ActorAction c01 -TimeoutSeconds 15 @rc | ConvertFrom-Json
if ($saved.player.tileX -eq $spawn.player.tileX -and $saved.player.tileY -eq $spawn.player.tileY) {
    throw 'The player did not leave the checkpoint spawn tile before saving.'
}
# Compare against the snapshot returned by the save operation itself. The actor
# can finish a movement step between the preceding wait response and this call.
$saved = & $client save $Slot @rc | ConvertFrom-Json

& $client scene 2 @rc | Out-Null
& $client wait -State Gameplay -City q01 -Scene xa -TimeoutSeconds 30 @rc | Out-Null
& $client load $Slot @rc | Out-Null
$loaded = & $client wait -State Gameplay -City q01 -Scene b -TimeoutSeconds 30 @rc | ConvertFrom-Json
if ($loaded.loadingSave -or $loaded.saveLoadError -or !$loaded.playerHasControl -or
    $loaded.story -ne $saved.story -or ($loaded.team -join '|') -ne ($saved.team -join '|') -or
    $loaded.player.layer -ne $saved.player.layer -or
    $loaded.player.tileX -ne $saved.player.tileX -or $loaded.player.tileY -ne $saved.player.tileY -or
    [Math]::Abs($loaded.player.x - $saved.player.x) -gt 0.02 -or
    [Math]::Abs($loaded.player.z - $saved.player.z) -gt 0.02) {
    throw "Save roundtrip mismatch. Saved: $($saved | ConvertTo-Json -Depth 8 -Compress); Loaded: $($loaded | ConvertTo-Json -Depth 8 -Compress)"
}

[pscustomobject]@{
    slot = $Slot
    scene = "$($loaded.city)/$($loaded.scene)"
    spawn = "$($spawn.player.tileX),$($spawn.player.tileY)"
    savedTile = "$($saved.player.tileX),$($saved.player.tileY)"
    restoredTile = "$($loaded.player.tileX),$($loaded.player.tileY)"
    story = $loaded.story
    team = $loaded.team
    playerHasControl = $loaded.playerHasControl
} | ConvertTo-Json -Depth 4
