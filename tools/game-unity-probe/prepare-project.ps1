<#
.SYNOPSIS
    Makes, or refreshes, the one scratch Unity project the player builds share, and returns its path.
    Only a folder this script made, and marked, is ever written to: its Assets are replaced on every
    call. Used by run.ps1 and tools/tester-package/build.ps1; never run two of them at once.
#>
param(
    [string]$Repo = (Resolve-Path "$PSScriptRoot/../..").Path,
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/game-unity-probe',
    [switch]$SkipSync
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $OutputRoot 'project'

# The adapter's DLLs from this checkout's Release build.
if (!$SkipSync) { & "$Repo/unity/sync-assemblies.ps1" | Out-Null }

if (Test-Path -LiteralPath $project) {
    if (!(Test-Path -LiteralPath "$project/.game-probe")) { throw "$project exists and is not the probe's scratch project" }
} else {
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    Set-Content -LiteralPath "$project/.game-probe" -Value 'isolated game profiling project'
}
# The first call seeds Library from a clone's, through a temporary name so a failed copy is retried.
if (!(Test-Path -LiteralPath "$project/Library")) {
    foreach ($cache in @("$Repo/unity/DK2.Unity/Library", 'D:/DungeonKeeperRemake/unity/DK2.Unity/Library')) {
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
    Copy-Item -LiteralPath "$Repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
}
Write-Output $project
