# Claude Code SessionStart hook: on Wednesdays and Fridays, if OpenKeeper has pull requests newer than
# the last one reviewed, ask the session to ask the owner whether to run the gap review. It never starts
# the review itself, asks at most once a day, and stays silent on any failure (no gh, no network).
#
# State: tools/openkeeper-review/last-reviewed.txt holds the highest PR number reviewed (tracked; update
# it when a review is done). .claude/openkeeper-review.local.json holds the day it last asked (ignored).
# -Day and -Force exist for testing: -Day Wednesday pretends today is Wednesday, -Force ignores the marker.
param([string]$Day = '', [switch]$Force)

$ErrorActionPreference = 'Stop'
try {
    $root = $env:CLAUDE_PROJECT_DIR
    if (-not $root) { $root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }

    $today = Get-Date
    $weekday = if ($Day) { $Day } else { $today.DayOfWeek.ToString() }
    if ($weekday -ne 'Wednesday' -and $weekday -ne 'Friday') { exit 0 }

    $stamp = $today.ToString('yyyy-MM-dd')
    $marker = Join-Path $root '.claude\openkeeper-review.local.json'
    if (-not $Force -and (Test-Path $marker)) {
        $asked = (Get-Content $marker -Raw | ConvertFrom-Json).asked
        if ($asked -eq $stamp) { exit 0 }
    }

    $last = [int](Get-Content (Join-Path $root 'tools\openkeeper-review\last-reviewed.txt') -TotalCount 1).Trim()
    $json = & gh pr list -R tonihele/OpenKeeper --state all --limit 200 --json number,state,body 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) { exit 0 }

    $new = @(($json | Out-String | ConvertFrom-Json) | Where-Object { $_.number -gt $last })
    Set-Content -Path $marker -Value (@{ asked = $stamp } | ConvertTo-Json -Compress) -Encoding ASCII
    if ($new.Count -eq 0) { exit 0 }

    $merged = @($new | Where-Object { $_.state -eq 'MERGED' }).Count
    $cited = @($new | Where-Object { $_.body -match '\b0x(00)?[4-6][0-9A-Fa-f]{5}\b' }).Count
    $top = ($new | Measure-Object -Property number -Maximum).Maximum
    $text = "OpenKeeper review reminder: $($new.Count) new OpenKeeper pull requests since #$last " +
        "(#$($last + 1)-#$top; $merged merged, $cited cite DKII.EXE addresses). " +
        "At the start of your first reply, ask the owner whether to run the OpenKeeper PR gap review now, " +
        "or write the Codex prompt for it. Do not start it unless they say yes. If a review is done, " +
        "write the highest reviewed PR number to tools/openkeeper-review/last-reviewed.txt."

    @{ hookSpecificOutput = @{ hookEventName = 'SessionStart'; additionalContext = $text } } |
        ConvertTo-Json -Compress -Depth 3
}
catch {
    exit 0
}
