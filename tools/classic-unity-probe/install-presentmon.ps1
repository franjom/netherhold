# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param([string]$Directory = "$PSScriptRoot/../../build/profiling-tools")
$ErrorActionPreference = 'Stop'
$name = 'PresentMon-2.6.0-x64.exe'
$expected = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
New-Item -ItemType Directory -Path $Directory -Force | Out-Null
$binary = Join-Path (Resolve-Path -LiteralPath $Directory).Path $name
if (!(Test-Path -LiteralPath $binary)) {
    Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/$name" -OutFile $binary
}
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $expected) {
    throw "PresentMon release hash mismatch: $binary"
}
$signature = Get-AuthenticodeSignature -LiteralPath $binary
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Intel Corporation') {
    throw "PresentMon publisher signature is not valid: $binary"
}
Write-Output "Verified portable PresentMon: $binary"
Write-Output 'License: https://github.com/GameTechDev/PresentMon/blob/v2.6.0/LICENSE.txt'
