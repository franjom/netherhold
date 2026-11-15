# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

# Starts the pinned Godot editor on godot/DK2.Godot. The working directory is the repository root
# on purpose: Godot AI's dock registers Claude Code against the editor's working directory, so a
# 'local'-scope entry lands on this repository and not on godot/DK2.Godot.
# Extra arguments go to Godot, e.g. -Console --headless --import, or -Console --headless --build-solutions --quit.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $InstallRoot = $(if ($env:DK2_GODOT_ROOT) { $env:DK2_GODOT_ROOT } else { Join-Path $env:LOCALAPPDATA 'Programs\Godot' }),
    # Use the console build: waits for exit and prints to this terminal (scripted and headless runs).
    [switch] $Console,
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $GodotArgs
)
$ErrorActionPreference = 'Stop'
$pin = Get-Content -Raw (Join-Path $PSScriptRoot 'godot-version.json') | ConvertFrom-Json
$exe = Join-Path (Join-Path $InstallRoot $pin.editor.version) $(if ($Console) { $pin.editor.windowsConsoleExe } else { $pin.editor.windowsExe })
if (-not (Test-Path $exe)) { throw "Godot $($pin.editor.version) is not installed at $exe. Run godot/setup.ps1 first." }
$repo = Split-Path -Parent $PSScriptRoot
# Godot AI reads this before its editor setting, for its own server and for the
# --disable-telemetry flag it writes into client entries (ADR-0810: telemetry off).
$env:GODOT_AI_DISABLE_TELEMETRY = 'true'
$arguments = @('--path', (Join-Path $PSScriptRoot 'DK2.Godot'))
if (-not $GodotArgs -or $GodotArgs -notcontains '--headless') { $arguments = @('--editor') + $arguments }
if ($GodotArgs) { $arguments += $GodotArgs }
Push-Location $repo
try {
    if ($Console) {
        & $exe @arguments
        exit $LASTEXITCODE
    }
    # Windows PowerShell does not quote -ArgumentList elements, so a path with spaces would split.
    $quoted = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    Start-Process -FilePath $exe -ArgumentList $quoted -WorkingDirectory $repo
} finally {
    Pop-Location
}
