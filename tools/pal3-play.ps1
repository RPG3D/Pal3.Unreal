[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('status', 'retry-boot', 'pose', 'list', 'slots', 'save', 'load', 'start', 'checkpoint', 'scene', 'scene-script', 'auto', 'advance', 'video', 'choose', 'overlay', 'demo', 'move', 'move-to', 'rotate-camera', 'touch-sticks', 'interact', 'wait')]
    [string] $Command,
    [Parameter(Position = 1)] [string] $Argument,
    [string] $Directory = $env:PAL3_AUTOMATION_DIR,
    [string] $RemoteUrl = 'http://127.0.0.1:30010',
    [string] $ObjectPath,
    [ValidateRange(1, 3600)] [int] $TimeoutSeconds = 60,
    [string] $State,
    [string] $City,
    [string] $Scene,
    [uint32] $ScriptId,
    [string] $LastCommand,
    [string] $Waiter,
    [string] $ActorAction,
    [string] $DialogueContains,
    [int] $TileX,
    [int] $TileY,
    [switch] $Choices
)
$ErrorActionPreference = 'Stop'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
if (!$ObjectPath) {
    if (!$Directory) { throw 'Pass -ObjectPath for UE Remote Control, or set PAL3_AUTOMATION_DIR for local requests.' }
    $sessionFile = Join-Path $Directory 'session.json'
    while (!(Test-Path -LiteralPath $sessionFile -PathType Leaf)) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw "No PAL3 session published at $sessionFile." }
        Start-Sleep -Milliseconds 150
    }
    $session = Get-Content -LiteralPath $sessionFile -Raw | ConvertFrom-Json
    if ($session.session -notmatch '^[0-9a-f]{32}$') { throw 'Invalid PAL3 session ID.' }
    $sessionDirectory = Join-Path (Resolve-Path -LiteralPath $Directory).Path $session.session
    $stopped = Join-Path $sessionDirectory 'stopped.json'
}

function Invoke-Pal3Request([string] $Name, [string] $Value) {
    if ($ObjectPath) {
        $body = @{
            objectPath = $ObjectPath
            functionName = 'ExecutePal3Command'
            parameters = @{ command = $Name; argument = $Value }
        } | ConvertTo-Json -Compress
        $remaining = [Math]::Max(1, [Math]::Ceiling(($deadline - [DateTimeOffset]::UtcNow).TotalSeconds))
        $native = Invoke-RestMethod -Method Put -Uri ($RemoteUrl.TrimEnd('/') + '/remote/object/call') `
            -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $remaining
        $reply = $native.ReturnValue | ConvertFrom-Json
        if (!$reply.ok) { throw $reply.error }
        return $reply.result
    }
    if (Test-Path -LiteralPath $stopped) { throw 'This PAL3 session has ended.' }
    $id = [Guid]::NewGuid().ToString('N')
    $request = Join-Path $sessionDirectory "$id.request.json"
    $response = Join-Path $sessionDirectory "$id.response.json"
    $temp = "$request.tmp"
    try {
        $payload = @{ command = $Name; argument = $Value; deadlineUtc = $deadline.ToString('o') } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($temp, $payload)
        [IO.File]::Move($temp, $request)
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            if (Test-Path -LiteralPath $response -PathType Leaf) {
                $reply = Get-Content -LiteralPath $response -Raw -Encoding UTF8 | ConvertFrom-Json
                if (!$reply.ok) { throw $reply.error }
                return $reply.result
            }
            if (Test-Path -LiteralPath $stopped) { throw 'PAL3 stopped while waiting for a response.' }
            Start-Sleep -Milliseconds 100
        }
        throw "Timed out waiting for '$Name'. Check PIE and the managed watchdog log."
    }
    finally {
        foreach ($path in @($temp, $request, $response)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
        }
    }
}

if ($Command -ne 'wait') {
    Invoke-Pal3Request $Command $Argument | ConvertTo-Json -Depth 12
    return
}

$hasScriptFilter = $PSBoundParameters.ContainsKey('ScriptId') -or $LastCommand -or $Waiter
$hasTileX = $PSBoundParameters.ContainsKey('TileX')
$hasTileY = $PSBoundParameters.ContainsKey('TileY')
if (!($State -or $City -or $Scene -or $ActorAction -or $DialogueContains -or $Choices -or
    $hasScriptFilter -or $hasTileX -or $hasTileY)) {
    throw 'wait requires a state, scene, tile, script, waiter, actor action, dialogue or choices filter.'
}

$lastStatus = $null
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    try {
        $lastStatus = Invoke-Pal3Request 'status' ''
    }
    catch {
        if ($null -ne $lastStatus) { Write-Host ($lastStatus | ConvertTo-Json -Depth 12 -Compress) }
        throw
    }
    $scriptMatched = !$hasScriptFilter -or @($lastStatus.scripts | Where-Object {
        (!$ScriptId -or $_.id -eq $ScriptId) -and
        (!$LastCommand -or $_.lastCommand -eq $LastCommand) -and
        (!$Waiter -or $_.waiter -eq $Waiter)
    }).Count -gt 0
    if ($scriptMatched -and (!$State -or $lastStatus.state -eq $State) -and
        (!$City -or $lastStatus.city -eq $City) -and (!$Scene -or $lastStatus.scene -eq $Scene) -and
        (!$hasTileX -or $lastStatus.player.tileX -eq $TileX) -and
        (!$hasTileY -or $lastStatus.player.tileY -eq $TileY) -and
        (!$ActorAction -or $lastStatus.player.action -eq $ActorAction) -and
        (!$Choices -or $lastStatus.choices.Count -gt 0) -and
        (!$DialogueContains -or ([string]$lastStatus.dialogue).Contains($DialogueContains))) {
        $lastStatus | ConvertTo-Json -Depth 12
        return
    }
    Start-Sleep -Milliseconds 100
}
throw "PAL3 wait condition timed out. Last status: $($lastStatus | ConvertTo-Json -Depth 12 -Compress)"
