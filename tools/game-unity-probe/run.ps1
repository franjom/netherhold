<#
.SYNOPSIS
    Profiles the game in a standalone Development player, headless, and prints where the slow
    frames went. See README.md.
#>
param(
    [string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/game-unity-probe',
    [string]$Level = 'Level1',
    [int]$Seed = 1,
    [int]$Seconds = 150,
    [int]$CaptureFrom = 0,
    [int]$CaptureTo = 0,
    [switch]$SkipSync
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$project = Join-Path $OutputRoot 'project'
$run = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $run -Force | Out-Null

# The adapter's DLLs from this checkout's Release build.
if (!$SkipSync) { & "$repo/unity/sync-assemblies.ps1" | Out-Null }

# One scratch project, reused for its import cache. Only a folder this script made, and marked,
# is ever written to: its Assets are replaced on every run.
if (Test-Path -LiteralPath $project) {
    if (!(Test-Path -LiteralPath "$project/.game-probe")) { throw "$project exists and is not the probe's scratch project" }
} else {
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    Set-Content -LiteralPath "$project/.game-probe" -Value 'isolated game profiling project'
}
# The first run seeds Library from a clone's, through a temporary name so a failed copy is retried.
if (!(Test-Path -LiteralPath "$project/Library")) {
    foreach ($cache in @("$repo/unity/DK2.Unity/Library", 'D:/DungeonKeeperRemake/unity/DK2.Unity/Library')) {
        if (!(Test-Path -LiteralPath $cache)) { continue }
        $partial = "$project/Library.partial"
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
        Copy-Item -LiteralPath $cache -Destination $partial -Recurse -Force
        Rename-Item -LiteralPath $partial -NewName 'Library'
        break
    }
}
if (Test-Path -LiteralPath "$project/Assets") { Remove-Item -LiteralPath "$project/Assets" -Recurse -Force }
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
}
Copy-Item -LiteralPath "$PSScriptRoot/GameProbe.cs" -Destination "$project/Assets/DK2/GameProbe.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/GameProbeBuild.cs" -Destination "$project/Assets/DK2/Editor/GameProbeBuild.cs" -Force

git -C $repo rev-parse HEAD | Set-Content -LiteralPath "$run/commit.txt"
git -C $repo status --short | Set-Content -LiteralPath "$run/worktree-status.txt"

function Run-Native([string]$Executable, [string[]]$Arguments, [int]$Minutes, [int[]]$Ok = @(0)) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddMinutes($Minutes)
    while (!$process.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) { $process.Kill(); throw "Timed out: $Executable; $run" }
    }
    if ($Ok -notcontains $process.ExitCode) { throw "Failed ($($process.ExitCode)): $Executable; see the logs in $run" }
}

$player = "$run/player/GameProbe.exe"
Write-Host "Building the Development player (first run imports the project; allow several minutes)..."
Run-Native $Unity @('-batchmode', '-quit', '-nographics', '-projectPath', $project,
    '-executeMethod', 'DK2.Unity.Editor.GameProbeBuild.Build', '-probeBuild', $player, '-logFile', "$run/build.log") 40
if (!(Test-Path -LiteralPath $player)) { throw "No player was built; see $run/build.log" }

Write-Host "Playing $Level for $Seconds s..."
Run-Native $player @('-batchmode', '-screen-width', '1280', '-screen-height', '720', '-dk2Install', $Install,
    '-level', $Level, '-seed', "$Seed", '-probeSeconds', "$Seconds", '-captureFrom', "$CaptureFrom",
    '-captureTo', "$CaptureTo", '-probeOutput', $run, '-logFile', "$run/player.log") ([math]::Ceiling($Seconds / 60) + 5) @(0, 2)
if (!(Test-Path -LiteralPath "$run/summary.txt")) { throw "No summary; see $run/player.log" }
Get-Content -LiteralPath "$run/summary.txt"
Write-Output $run
if ((Get-Content -LiteralPath "$run/summary.txt" -TotalCount 1) -like 'INVALID RUN*') {
    Write-Warning 'The game logged errors during the run; its timings are not usable.'
    exit 2
}
