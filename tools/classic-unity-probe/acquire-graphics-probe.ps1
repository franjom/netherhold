# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
$directory = Join-Path $RepositoryRoot 'build'
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    [IO.File]::Open((Join-Path $directory 'graphics-probe.lock'), [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
} catch [IO.IOException] {
    throw 'Another graphics probe is running. Rendered performance captures and Unity verification must run serially.'
}
