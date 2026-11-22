[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$version = '1.18.35'
$runtime = Join-Path $PSScriptRoot '.runtime'
$env:XDG_CONFIG_HOME = Join-Path $runtime 'config'
$env:XDG_DATA_HOME = Join-Path $runtime 'data'
$env:XDG_CACHE_HOME = Join-Path $runtime 'cache'
$env:XDG_STATE_HOME = Join-Path $runtime 'state'
if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
    throw 'Install Node.js with npm before running this script.'
}
# No lifecycle scripts, global installation, model weights or inference runtime.
& npm.cmd install --prefix $runtime --no-audit --no-fund --ignore-scripts "opencode-ai@$version"
if ($LASTEXITCODE -ne 0) { throw 'OpenCode package installation failed.' }
$executable = Join-Path $runtime 'node_modules/opencode-windows-x64/bin/opencode.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'This setup script requires Windows x64.' }
$actual = & $executable --version
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $version) { throw 'OpenCode version verification failed.' }
Write-Output "Installed OpenCode $actual at $executable"
Write-Output 'Configure QWEN_* environment variables, then run python tools/qwen/delegate.py validate-api.'
