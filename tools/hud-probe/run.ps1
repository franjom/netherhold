# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$Project = 'D:/DungeonKeeperRemake/build/hud-toolkit-pilot',
    [string]$Output = 'D:/DungeonKeeperRemake/build/hud-toolkit-results'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$allowedRoot = [IO.Path]::GetFullPath('D:/DungeonKeeperRemake/build') + [IO.Path]::DirectorySeparatorChar
foreach ($candidate in @($Project, $Output)) {
    $absolute = [IO.Path]::GetFullPath($candidate)
    if (!$absolute.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Outside scratch build root: $absolute" }
    $ancestor = $absolute
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Filesystem link in scratch path: $ancestor"
        }
        $parent = Split-Path -Parent $ancestor
        if ($parent -eq $ancestor) { break }
        $ancestor = $parent
    }
}
if ((Test-Path -LiteralPath $Project) -and !(Test-Path -LiteralPath "$Project/.dk2-hud-pilot")) {
    throw 'Existing project is not marked as this isolated HUD pilot.'
}
foreach ($editor in Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") {
    if ($editor.CommandLine -and $editor.CommandLine.Replace('\','/').Contains($Project.Replace('\','/'))) {
        throw 'The isolated HUD project is already open; close that editor before rerunning.'
    }
}
New-Item -ItemType Directory -Force -Path $Project, $Output | Out-Null
New-Item -ItemType File -Force -Path "$Project/.dk2-hud-pilot" | Out-Null
foreach ($relative in @('Assets/HudProbe/Editor','Assets/HudProbe/Resources','Assets/Plugins/DK2','Assets/DK2/Rendering','Packages','ProjectSettings')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Project $relative) | Out-Null
}
$buildLog = Join-Path $Output 'compile.log'
& dotnet build "$PSScriptRoot/HudPilot.Compile.csproj" -c Release -m:1 -p:NuGetAudit=false --nologo -v q *> $buildLog
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog -Tail 20; throw 'HUD pilot compile failed.' }
foreach ($name in @('DK2.Core','DK2.Data.Model','DK2.Data','DK2.Assets','DK2.Pathfinding','DK2.Simulation','DK2.Presentation')) {
    Copy-Item -LiteralPath "$repo/src/$name/bin/Release/netstandard2.1/$name.dll" -Destination "$Project/Assets/Plugins/DK2" -Force
}
foreach ($name in @('HudPilotAssets.cs','HudFontPilot.cs','HudToolkitPilot.cs','HudPilot.shader')) {
    Copy-Item -LiteralPath "$PSScriptRoot/$name" -Destination "$Project/Assets/HudProbe" -Force
}
Copy-Item -LiteralPath "$PSScriptRoot/HudPilotEntry.cs" -Destination "$Project/Assets/HudProbe/Editor" -Force
Copy-Item -LiteralPath "$PSScriptRoot/HudPilot.tss" -Destination "$Project/Assets/HudProbe/Resources" -Force
Copy-Item "$repo/unity/DK2.Unity/Packages/*" -Destination "$Project/Packages" -Force
$manifestPath = Join-Path $Project 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.dependencies | Add-Member -NotePropertyName 'com.unity.modules.imageconversion' -NotePropertyValue '1.0.0' -Force
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 100), (New-Object Text.UTF8Encoding($false)))
Copy-Item "$repo/unity/DK2.Unity/ProjectSettings/*" -Destination "$Project/ProjectSettings" -Force
Copy-Item "$repo/unity/DK2.Unity/Assets/DK2/Rendering/*" -Destination "$Project/Assets/DK2/Rendering" -Force
foreach ($name in @('UniversalRenderPipelineGlobalSettings.asset','DefaultVolumeProfile.asset')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/Assets/$name" -Destination "$Project/Assets" -Force
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/Assets/$name.meta" -Destination "$Project/Assets" -Force
}
$env:DK2_DIR = $Install
$env:DK2_HUD_PROBE_OUTPUT = [IO.Path]::GetFullPath($Output)
$log = Join-Path $Output 'unity.log'
$arguments = @('-batchmode','-projectPath',('"' + $Project + '"'),'-executeMethod',
    'DK2.Unity.HudProbe.Editor.HudPilotEntry.Run','-job-worker-count','2',
    '-background-job-worker-count','2','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = (Get-Date).AddMinutes(6)
while (!$process.WaitForExit(500)) {
    if ((Get-Date) -gt $deadline) { $process.Kill(); throw "Own HUD pilot exceeded timeout; see $log" }
}
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $log -Tail 18; throw "Unity pilot exited $($process.ExitCode)." }
Get-Content -LiteralPath (Join-Path $Output 'result.json')
