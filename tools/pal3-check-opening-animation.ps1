[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ObjectPath,
    [string] $RemoteUrl = 'http://127.0.0.1:30010'
)

$ErrorActionPreference = 'Stop'
$client = Join-Path $PSScriptRoot 'pal3-play.ps1'
$rc = @{ ObjectPath = $ObjectPath; RemoteUrl = $RemoteUrl }

function Wait-AnimatedPose([int] $ActorId, [string] $Action, [int] $Seconds, [switch] $ExpectMovement) {
    $until = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    $fingerprints = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $first = $null
    $previous = $null
    $firstMovementHeading = $null
    $bestAlignment = 0.0
    $maxAlignment = 0.0
    $maxPoses = 0
    $maxTravel = 0.0
    while ([DateTimeOffset]::UtcNow -lt $until) {
        $sample = & $client pose ([string]$ActorId) -TimeoutSeconds 5 @rc | ConvertFrom-Json
        if ($sample.action -ne $Action) {
            $first = $null
            $previous = $null
            $bestAlignment = 0.0
            $fingerprints.Clear()
        }
        elseif ($sample.fingerprints.Count -gt 0 -and '' -notin $sample.fingerprints) {
            if ($null -eq $first) { $first = $sample }
            $null = $fingerprints.Add(($sample.fingerprints -join '|'))
            $dx = $sample.x - $first.x
            $dz = $sample.z - $first.z
            $travel = [Math]::Sqrt($dx * $dx + $dz * $dz)
            $maxPoses = [Math]::Max($maxPoses, $fingerprints.Count)
            $maxTravel = [Math]::Max($maxTravel, $travel)
            $headingDot = 1.0
            if ($ExpectMovement -and $sample.forward) {
                if (!$firstMovementHeading) { $firstMovementHeading = $sample.forward }
                if ($previous) {
                    $stepX = $sample.x - $previous.x
                    $stepZ = $sample.z - $previous.z
                    $stepDistance = [Math]::Sqrt($stepX * $stepX + $stepZ * $stepZ)
                    if ($stepDistance -gt 0.01 -and $stepDistance -lt 1.2) {
                        $alignment = ($stepX * $sample.forward.x + $stepZ * $sample.forward.z) / $stepDistance
                        $bestAlignment = [Math]::Max($bestAlignment, $alignment)
                        $maxAlignment = [Math]::Max($maxAlignment, $alignment)
                    }
                }
                $headingDot = $firstMovementHeading.x * $sample.forward.x + $firstMovementHeading.z * $sample.forward.z
            }
            $previous = $sample
            $movementMatches = $travel -ge 0.05 -and $bestAlignment -ge 0.95 -and $headingDot -le 0.95
            if ($fingerprints.Count -ge 3 -and (!$ExpectMovement -or $movementMatches)) {
                return [pscustomobject]@{
                    actorId = $ActorId
                    action = $Action
                    distinctNativePoses = $fingerprints.Count
                    horizontalTravel = [Math]::Round($travel, 3)
                    bestMovementAlignment = [Math]::Round($bestAlignment, 3)
                    initialHeadingDot = [Math]::Round($headingDot, 3)
                    firstSample = $first.simulationSeconds
                    lastSample = $sample.simulationSeconds
                }
            }
        }
        Start-Sleep -Milliseconds 35
    }
    throw "Actor $ActorId action $Action did not reach the expected pose or movement. Max poses: $maxPoses, horizontal travel: $maxTravel, best movement alignment: $maxAlignment."
}

$initial = & $client status @rc | ConvertFrom-Json
try {
    & $client auto true @rc | Out-Null
    & $client start @rc | Out-Null
    & $client wait -ScriptId 1001 -DialogueContains '有贼' -TimeoutSeconds 70 @rc | Out-Null

    $jingTian = Wait-AnimatedPose 0 z19 20
    $xueJian = Wait-AnimatedPose 1 c02 50 -ExpectMovement

    [pscustomobject]@{ jingTian = $jingTian; xueJian = $xueJian } | ConvertTo-Json -Depth 4
}
finally {
    & $client auto ([string]$initial.autoDialogue) @rc | Out-Null
}
