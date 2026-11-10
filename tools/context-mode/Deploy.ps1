# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.

param(
    [string]$Profile = 'local',
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path

docker build --tag dk2/context-mode:2.1.7 $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Context-mode image build failed.' }

# Docker accepts local definitions only below its trusted catalog directory.
$catalogDirectory = Join-Path $env:USERPROFILE '.docker/mcp/catalogs'
[IO.Directory]::CreateDirectory($catalogDirectory) | Out-Null
$definitionPath = Join-Path $catalogDirectory ('context-mode-' + [guid]::NewGuid() + '.yaml')
[IO.File]::Copy((Join-Path $PSScriptRoot 'server.yaml'), $definitionPath, $false)
$definitionUri = 'file://' + $definitionPath.Replace('\', '/')

# Re-running upgrades in place: drop the previous registration (a no-op when absent).
docker mcp profile server remove $Profile context-mode *> $null
$global:LASTEXITCODE = 0

docker mcp profile server add $Profile --server $definitionUri
if ($LASTEXITCODE -ne 0) { throw "Server registration failed; definition: $definitionPath" }
docker mcp profile config $Profile --set "context-mode.project_path=$ProjectPath"
if ($LASTEXITCODE -ne 0) { throw 'Project mount configuration failed.' }

docker mcp profile tools $Profile --disable-all context-mode
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the context-mode tool allowlist.' }
$toolArguments = @('mcp', 'profile', 'tools', $Profile)
foreach ($toolName in @('ctx_execute', 'ctx_batch', 'ctx_index', 'ctx_search', 'ctx_stats')) {
    $toolArguments += @('--enable', "context-mode.$toolName")
}
docker @toolArguments
if ($LASTEXITCODE -ne 0) { throw 'Could not enable the context-mode analysis tools.' }
Write-Output "Context-mode registered in profile '$Profile'; definition: $definitionPath"
