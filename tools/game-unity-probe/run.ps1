<#
.SYNOPSIS
    Profiles the game in a standalone Development player, headless, and prints where the slow
    frames went. See README.md.
#>
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/game-unity-probe',
    [string]$Level = 'Level1',
    [int]$Seed = 1,
    [int]$Seconds = 150,
    [int]$CaptureFrom = 0,
    [int]$CaptureTo = 0,
    [string]$Markers = '',
    [switch]$WorkerTicks,
    [switch]$Il2cpp,
    [switch]$RecordFrames,
    [switch]$SkipSync
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $run -Force | Out-Null

# The shared scratch project, refreshed from this checkout (prepare-project.ps1).
$project = & "$PSScriptRoot/prepare-project.ps1" -Repo $repo -OutputRoot $OutputRoot -SkipSync:$SkipSync
Copy-Item -LiteralPath "$PSScriptRoot/GameProbe.cs" -Destination "$project/Assets/DK2/GameProbe.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/GameProbeBuild.cs" -Destination "$project/Assets/DK2/Editor/GameProbeBuild.cs" -Force

$backend = $(if ($Il2cpp) { 'il2cpp' } else { 'mono' })
Set-Content -LiteralPath "$run/backend.txt" -Value $backend
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

# The IL2CPP module is per editor install; the Hub copy of 6000.6.0f1 has it (ADR-0690).
if ($Il2cpp -and !(Test-Path -LiteralPath (Join-Path (Split-Path $Unity) 'Data/PlaybackEngines/windowsstandalonesupport/Variations/win64_player_development_il2cpp'))) {
    throw "$Unity has no Windows IL2CPP module; install it for that editor (Unity Hub > Installs > Add modules) or pass -Unity for an install that has it"
}
$player = "$run/player/GameProbe.exe"
Write-Host "Building the $backend Development player (first run imports the project; IL2CPP compiles C++; allow several minutes)..."
Run-Native $Unity @('-batchmode', '-quit', '-nographics', '-projectPath', $project,
    '-executeMethod', 'DK2.Unity.Editor.GameProbeBuild.Build', '-probeBuild', $player, '-probeBackend', $backend,
    '-logFile', "$run/build.log") 60
if (!(Test-Path -LiteralPath $player)) { throw "No player was built; see $run/build.log" }

Write-Host "Playing $Level for $Seconds s..."
$playerArguments = @('-batchmode', '-screen-width', '1280', '-screen-height', '720', '-dk2Install', $Install,
    '-level', $Level, '-seed', "$Seed", '-probeSeconds', "$Seconds", '-captureFrom', "$CaptureFrom",
    '-captureTo', "$CaptureTo", '-markers', $(if ($Markers) { $Markers } else { ',' }), '-workerTicks', $(if ($WorkerTicks) { '1' } else { '0' }),
    '-probeOutput', $run, '-logFile', "$run/player.log")
if ($RecordFrames) { $playerArguments += @('-recordFrames', "$run/recorder") }
Run-Native $player $playerArguments ([math]::Ceiling($Seconds / 60) + 5) @(0, 2)
if (!(Test-Path -LiteralPath "$run/summary.txt")) { throw "No summary; see $run/player.log" }
Get-Content -LiteralPath "$run/summary.txt"
Write-Output $run
if ((Get-Content -LiteralPath "$run/summary.txt" -TotalCount 1) -like 'INVALID RUN*') {
    Write-Warning 'The game logged errors during the run; its timings are not usable.'
    exit 2
}
