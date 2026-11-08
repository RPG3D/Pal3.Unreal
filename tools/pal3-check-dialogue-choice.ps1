[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ObjectPath,
    [string] $RemoteUrl = 'http://127.0.0.1:30010'
)
$ErrorActionPreference = 'Stop'
$client = Join-Path $PSScriptRoot 'pal3-play.ps1'
$rc = @{ ObjectPath = $ObjectPath; RemoteUrl = $RemoteUrl }
$initial = & $client status @rc | ConvertFrom-Json
try {
    & $client auto true @rc | Out-Null
    & $client checkpoint 1 @rc | Out-Null
    $spawn = & $client wait -State Gameplay -City q01 -Scene b -TimeoutSeconds 30 @rc | ConvertFrom-Json

    try {
        & $client move-to '75 346 run' @rc | Out-Null
        throw 'A blocked destination was accepted.'
    }
    catch {
        if ($_.Exception.Message -notlike '*not walkable*') { throw }
    }

    # Walkable tile in original scene object 71's automatic trigger rectangle.
    & $client move-to '81 357 run' @rc | Out-Null
    $question = & $client wait -Choices -ScriptId 287 -TimeoutSeconds 70 @rc | ConvertFrom-Json
    if ($question.choices.Count -ne 2 -or !$question.choicePrompt.Contains('竹筏')) {
        throw 'The raft prompt and its two choices were not published.'
    }
    if ($question.player.layer -ne 0 -or $question.player.tileX -lt 75 -or $question.player.tileX -gt 82 -or
        $question.player.tileY -lt 346 -or $question.player.tileY -gt 363) {
        throw 'The raft prompt did not start inside the original trigger rectangle.'
    }
    try {
        & $client choose 9 @rc | Out-Null
        throw 'Out-of-range choice was accepted.'
    }
    catch {
        if ($_.Exception.Message -notlike '*valid 1-based index*') { throw }
    }

    & $client choose 2 @rc | Out-Null
    $no = & $client wait -State Gameplay -City q01 -Scene b -TimeoutSeconds 80 @rc | ConvertFrom-Json
    if (!$no.playerHasControl) { throw 'The No branch did not return player control.' }

    & $client move-to '81 357 run' @rc | Out-Null
    & $client wait -Choices -ScriptId 287 -TimeoutSeconds 35 @rc | Out-Null
    & $client choose 1 @rc | Out-Null
    $yes = & $client wait -State Gameplay -City q01 -Scene x -TimeoutSeconds 50 @rc | ConvertFrom-Json
    if (!$yes.playerHasControl) { throw 'The Yes branch did not return player control.' }

    # The destination has its own raft trigger (scene object 15 / script 288).
    & $client move-to '206 5 walk' @rc | Out-Null
    $returnQuestion = & $client wait -Choices -ScriptId 288 -TimeoutSeconds 35 @rc | ConvertFrom-Json
    if ($returnQuestion.player.layer -ne 0 -or
        $returnQuestion.player.tileX -lt 202 -or $returnQuestion.player.tileX -gt 211 -or
        $returnQuestion.player.tileY -lt 1 -or $returnQuestion.player.tileY -gt 9) {
        throw 'The return prompt did not start inside the original return trigger.'
    }
    & $client choose 1 @rc | Out-Null
    $returned = & $client wait -State Gameplay -City q01 -Scene b -TimeoutSeconds 50 @rc | ConvertFrom-Json
    if (!$returned.playerHasControl) { throw 'The return trip did not return player control.' }

    [pscustomobject]@{
        prompt = $question.choicePrompt
        choices = $question.choices
        spawn = "$($spawn.player.tileX),$($spawn.player.tileY)"
        trigger = "$($question.player.tileX),$($question.player.tileY)"
        no = "$($no.city)/$($no.scene)"
        yes = "$($yes.city)/$($yes.scene)"
        returnTrigger = "$($returnQuestion.player.tileX),$($returnQuestion.player.tileY)"
        returned = "$($returned.city)/$($returned.scene)"
        playerHasControl = $returned.playerHasControl
    } | ConvertTo-Json -Depth 4
}
finally {
    & $client auto ([string]$initial.autoDialogue) @rc | Out-Null
}
