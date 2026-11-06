<#
.SYNOPSIS
    Builds the tester package: a release player of the game (IL2CPP), the pinned PresentMon, the
    capture script and its instructions, zipped for someone on other hardware (docs/PERFORMANCE.md,
    the desktop tier). See README.md.
#>
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/tester-package',
    [switch]$SkipSync
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$package = Join-Path $OutputRoot $stamp
New-Item -ItemType Directory -Path "$package/tools" -Force | Out-Null

if (!(Test-Path -LiteralPath (Join-Path (Split-Path $Unity) 'Data/PlaybackEngines/windowsstandalonesupport/Variations/win64_player_nondevelopment_il2cpp'))) {
    throw "$Unity has no Windows IL2CPP module (ADR-0690); install it or pass -Unity for an install that has it"
}

# The shared scratch project (never alongside a game-unity-probe run), without the probe's sources.
$project = & "$repo/tools/game-unity-probe/prepare-project.ps1" -Repo $repo -SkipSync:$SkipSync
Copy-Item -LiteralPath "$PSScriptRoot/ReleasePlayerBuild.cs" -Destination "$project/Assets/DK2/Editor/ReleasePlayerBuild.cs" -Force

function Run-Native([string]$Executable, [string[]]$Arguments, [int]$Minutes) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddMinutes($Minutes)
    while (!$process.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) { $process.Kill(); throw "Timed out: $Executable; $package" }
    }
    if ($process.ExitCode -ne 0) { throw "Failed ($($process.ExitCode)): $Executable; see the logs in $package" }
}

$player = "$package/player/DK2.exe"
Write-Host "Building the release player (IL2CPP compiles C++; allow several minutes)..."
Run-Native $Unity @('-batchmode', '-quit', '-nographics', '-projectPath', $project,
    '-executeMethod', 'DK2.Unity.Editor.ReleasePlayerBuild.Build', '-playerBuild', $player, '-logFile', "$package/build.log") 60
if (!(Test-Path -LiteralPath $player)) { throw "No player was built; see $package/build.log" }
# The IL2CPP symbols folder is for crash dumps here, not for the tester: it is most of the size.
Get-ChildItem -LiteralPath "$package/player" -Directory -Filter '*BackUpThisFolder*' | Remove-Item -Recurse -Force

# The pinned PresentMon (hash and publisher signature checked by the installer).
$presentMon = "$repo/build/profiling-tools/PresentMon-2.6.0-x64.exe"
& "$repo/tools/classic-unity-probe/install-presentmon.ps1" | Out-Null
Copy-Item -LiteralPath $presentMon -Destination "$package/tools/PresentMon-2.6.0-x64.exe" -Force

Copy-Item -LiteralPath "$PSScriptRoot/capture.ps1" -Destination "$package/capture.ps1" -Force
Copy-Item -LiteralPath "$PSScriptRoot/package-README.md" -Destination "$package/README.txt" -Force

# What the package was built from, so a capture names its build.
$commit = git -C $repo rev-parse HEAD
$dirty = git -C $repo status --short
$hashes = @(Get-ChildItem -LiteralPath "$package/player" -Recurse -File | Where-Object { $_.Name -in @('DK2.exe', 'GameAssembly.dll', 'UnityPlayer.dll') }) +
    @(Get-ChildItem -LiteralPath "$repo/unity/DK2.Unity/Assets/Plugins/DK2" -Filter '*.dll')
$manifest = @("Package=$stamp", "Commit=$commit", "WorktreeClean=$([string]::IsNullOrWhiteSpace($dirty))",
    "Unity=$((Get-Content -LiteralPath "$repo/unity/DK2.Unity/ProjectSettings/ProjectVersion.txt" -TotalCount 1) -replace '^m_EditorVersion:\s*', '')",
    "Backend=IL2CPP", "Development=False", "PresentMon=2.6.0") +
    @($hashes | ForEach-Object { "SHA256 $($_.Name)=$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" })
Set-Content -LiteralPath "$package/manifest.txt" -Value $manifest
Set-Content -LiteralPath "$package/build-commit.txt" -Value $commit
if ($dirty) { Set-Content -LiteralPath "$package/worktree-status.txt" -Value $dirty }
Remove-Item -LiteralPath "$package/build.log" -Force

$zip = Join-Path $OutputRoot "dk2-tester-$stamp.zip"
Compress-Archive -Path "$package/*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Package: $zip"
Write-Output $zip
