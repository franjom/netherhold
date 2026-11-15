# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [string]$Project = "$PSScriptRoot/../../build/tag-overlay-probe/project",
    [string]$Log = "$PSScriptRoot/../../build/hover-outline-probe.log"
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
if (!(Test-Path -LiteralPath "$Project/.tag-overlay-probe")) {
    throw 'Requires an existing isolated tag-overlay probe project.'
}
$graphicsLease = & "$PSScriptRoot/acquire-graphics-probe.ps1" -RepositoryRoot $repo
try {
    Copy-Item -LiteralPath "$PSScriptRoot/HoverOutlineFiniteProbe.cs" -Destination "$Project/Assets/DK2/Editor/HoverOutlineFiniteProbe.cs" -Force
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/Assets/DK2/Resources/Shaders/ClassicHoverOutline.shader" -Destination "$Project/Assets/DK2/Resources/Shaders/ClassicHoverOutline.shader" -Force
    $arguments = @('-batchmode', '-force-d3d11', '-projectPath', [IO.Path]::GetFullPath($Project),
        '-executeMethod', 'DK2.Unity.Editor.HoverOutlineFiniteProbe.Run', '-logFile', [IO.Path]::GetFullPath($Log))
    $quoted = foreach ($argument in $arguments) { '"' + $argument + '"' }
    $process = Start-Process -FilePath $Unity -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(600000)) { $process.Kill(); throw "Probe timed out: $Log" }
    $lines = Get-Content -LiteralPath $Log
    $profile = @($lines | Where-Object { $_ -match '^Hover profile' })
    $raster = @($lines | Where-Object { $_ -match '^Hover raster' })
    $invalid = @($lines | Where-Object { $_ -match '^Hover (profile|raster).*invalid=[1-9]' })
    Write-Output "GPU cases: $($profile.Count) profile, $($raster.Count) raster; invalid cases: $($invalid.Count)."
    $lines | Where-Object { $_ -match 'Hover outline|error CS' }
    if ($process.ExitCode -ne 0) { throw "Probe failed ($($process.ExitCode)): $Log" }
} finally {
    $graphicsLease.Dispose()
}
