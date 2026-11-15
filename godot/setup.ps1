# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

# Installs the Godot editor pinned in godot-version.json, side by side with any other version,
# installs the Godot AI addon (gitignored agent tooling) and checks that both agree with the pin.
# Safe to rerun. The addon is extracted only when addons/godot_ai is absent: Godot AI forbids
# extracting a release over an existing tree. Update it with the dock's Update button, then pin
# the new version here; or delete the folder, change the pin and rerun.
[CmdletBinding()]
param(
    # Where editors are unpacked, one folder per version. DK2_GODOT_ROOT overrides the default.
    [string] $InstallRoot = $(if ($env:DK2_GODOT_ROOT) { $env:DK2_GODOT_ROOT } else { Join-Path $env:LOCALAPPDATA 'Programs\Godot' })
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times slower with the progress bar
$pin = Get-Content -Raw (Join-Path $PSScriptRoot 'godot-version.json') | ConvertFrom-Json
$project = Join-Path $PSScriptRoot 'DK2.Godot'

function Get-Verified([string] $url, [string] $file, [string] $algorithm, [string] $expected) {
    if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm $algorithm).Hash -ne $expected) {
        Write-Host "Downloading $url"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $file
    }
    $actual = (Get-FileHash $file -Algorithm $algorithm).Hash
    if ($actual -ne $expected) {
        Remove-Item $file
        throw "$algorithm of $url is $actual, the pin says $expected. Not installing it."
    }
}

# 1. The editor (.NET build), one folder per version so an upgrade never touches the old one.
$editorDir = Join-Path $InstallRoot $pin.editor.version
$exe = Join-Path $editorDir $pin.editor.windowsExe
if (Test-Path $exe) {
    Write-Host "Godot $($pin.editor.version) is installed: $exe"
} else {
    # Unpack beside the target and rename it into place, so an interrupted run never leaves a
    # partial folder that the Test-Path above would take for an installed editor.
    if (Test-Path $editorDir) { throw "$editorDir exists without $($pin.editor.windowsExe); delete it and rerun." }
    $staging = "$editorDir.partial"
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    New-Item -ItemType Directory -Force $staging | Out-Null
    $zip = Join-Path $staging $pin.editor.windowsZip
    Get-Verified ($pin.editor.releaseUrl + $pin.editor.windowsZip) $zip 'SHA512' $pin.editor.windowsZipSha512
    Expand-Archive -Path $zip -DestinationPath $staging
    Remove-Item $zip
    if (-not (Test-Path (Join-Path $staging $pin.editor.windowsExe))) { throw "The archive did not contain $($pin.editor.windowsExe)." }
    Rename-Item $staging (Split-Path -Leaf $editorDir)
    Write-Host "Installed Godot $($pin.editor.version): $exe"
}

# 2. The Godot AI addon, only when absent. The archive is pinned by SHA-256; its files are also
#    cross-checked against the release manifest (its signature is not checked here; the dock's
#    updater checks signatures). Staged on the project's volume so the final move is a rename.
$addon = Join-Path $project 'addons\godot_ai'
if (-not (Test-Path $addon)) {
    $tmp = Join-Path $project (".godot\godot-ai-install-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $zip = Join-Path $tmp $pin.godotAi.zip
        Get-Verified ($pin.godotAi.releaseUrl + $pin.godotAi.zip) $zip 'SHA256' $pin.godotAi.zipSha256
        $manifestFile = Join-Path $tmp $pin.godotAi.manifest
        Invoke-WebRequest -UseBasicParsing -Uri ($pin.godotAi.releaseUrl + $pin.godotAi.manifest) -OutFile $manifestFile
        $manifest = Get-Content -Raw $manifestFile | ConvertFrom-Json
        if ($manifest.version -ne $pin.godotAi.version -or $manifest.asset.sha256 -ne $pin.godotAi.zipSha256) {
            throw 'The Godot AI release manifest does not match the pin.'
        }
        $unpacked = Join-Path $tmp 'unpacked'
        Expand-Archive -Path $zip -DestinationPath $unpacked
        foreach ($entry in $manifest.inventory) {
            $file = Join-Path $unpacked $entry.path
            if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Hash mismatch: $($entry.path)" }
        }
        New-Item -ItemType Directory -Force (Split-Path $addon) | Out-Null
        Move-Item (Join-Path $unpacked 'addons\godot_ai') $addon
        Write-Host "Installed Godot AI $($pin.godotAi.version) into $addon"
    } finally {
        Remove-Item -Recurse -Force $tmp
    }
}

# 3. Drift between the pin and what the project says (the editor rewrites both on upgrade).
$sdk = ([xml](Get-Content -Raw (Join-Path $project 'DK2.Godot.csproj'))).Project.Sdk
if ($sdk -ne "Godot.NET.Sdk/$($pin.godotNetSdk)") { Write-Warning "DK2.Godot.csproj uses $sdk; godot-version.json pins Godot.NET.Sdk/$($pin.godotNetSdk)." }
$cfg = Get-Content (Join-Path $addon 'plugin.cfg') | Where-Object { $_ -match '^version=' }
if ($cfg -ne "version=""$($pin.godotAi.version)""") { Write-Warning "addons/godot_ai is $cfg; godot-version.json pins $($pin.godotAi.version). Update the pin." }

Write-Host 'Open the project with: powershell -File godot/editor.ps1'
