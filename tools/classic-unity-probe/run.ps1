param(
    [string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/classic-unity-probe',
    [switch]$ReuseLastProject,
    [switch]$BaselineHead,
    [string]$Creature = 'Imp',
    [ValidateSet('cpu', 'vat', 'frames')][string]$Representation = 'cpu'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$run = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
$project = Join-Path $run 'project'
if ($ReuseLastProject) {
    $last = Get-ChildItem -LiteralPath $OutputRoot -Directory | Where-Object { Test-Path "$($_.FullName)/project/.classic-crowd-probe" } | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $last -or !(Test-Path "$($last.FullName)/project")) { throw 'No scratch project to reuse' }
    $project = Join-Path $last.FullName 'project'
}
New-Item -ItemType Directory -Path $run -Force | Out-Null
New-Item -ItemType Directory -Path $project -Force | Out-Null
Set-Content -LiteralPath "$project/.classic-crowd-probe" -Value 'isolated performance project'
Set-Content -LiteralPath "$run/project-path.txt" -Value $project
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$repo/unity/DK2.Unity/$folder" -Destination $project -Recurse -Force
}
if ($BaselineHead) {
    foreach ($trackedPath in (git -C $repo ls-files 'unity/DK2.Unity/Assets/*.cs')) {
        $relative = $trackedPath.Substring('unity/DK2.Unity/'.Length)
        git -C $repo show "HEAD:$trackedPath" | Set-Content -LiteralPath (Join-Path $project $relative) -Encoding UTF8
    }
}
Copy-Item -LiteralPath "$PSScriptRoot/ClassicCrowdProbe.cs" -Destination "$project/Assets/DK2/ClassicCrowdProbe.cs"
Copy-Item -LiteralPath "$PSScriptRoot/ClassicCrowdAlternative.cs" -Destination "$project/Assets/DK2/ClassicCrowdAlternative.cs"
Copy-Item -LiteralPath "$PSScriptRoot/CrowdVAT.shader" -Destination "$project/Assets/DK2/CrowdVAT.shader"
Copy-Item -LiteralPath "$PSScriptRoot/ClassicCrowdBuild.cs" -Destination "$project/Assets/DK2/Editor/ClassicCrowdBuild.cs"
$manifestPath = "$project/Packages/manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.dependencies | Add-Member -NotePropertyName 'com.unity.modules.imageconversion' -NotePropertyValue '1.0.0' -Force
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 50), (New-Object System.Text.UTF8Encoding($false)))
$assemblyPath = "$project/Assets/DK2/DK2.Unity.asmdef"
$assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
$assembly.references = @($assembly.references) + @('Unity.RenderPipelines.Universal.Runtime')
[System.IO.File]::WriteAllText($assemblyPath, ($assembly | ConvertTo-Json -Depth 50), (New-Object System.Text.UTF8Encoding($false)))
git -C $repo rev-parse HEAD | Set-Content "$run/commit.txt"
Set-Content -LiteralPath "$run/source-snapshot.txt" -Value $(if ($BaselineHead) { 'HEAD Unity C# sources, copied installed DLLs' } else { 'working tree Unity C# sources, copied installed DLLs' })
$snapshotProcess = Start-Process -FilePath (Get-Command git).Source -ArgumentList @('-C', ('"' + $repo + '"'), 'diff', 'HEAD') `
    -RedirectStandardOutput "$run/worktree.diff" -RedirectStandardError "$run/git-diff-warnings.log" -WindowStyle Hidden -Wait -PassThru
if ($snapshotProcess.ExitCode -ne 0) { throw "Cannot capture worktree diff: $run/git-diff-warnings.log" }
Get-ChildItem -LiteralPath "$project/Assets/Plugins/DK2" -Filter '*.dll' |
    Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/assembly-hashes.csv" -NoTypeInformation
Get-ChildItem -LiteralPath "$project/Assets/DK2" -Filter '*.cs' -Recurse |
    Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash | Export-Csv "$run/source-hashes.csv" -NoTypeInformation
function Run-Native([string]$Executable, [string[]]$Arguments) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(1200000)) {
        $process.Kill()
        throw "Probe process exceeded 20 minutes: $Executable"
    }
    if ($process.ExitCode -ne 0) { throw "Probe process failed ($($process.ExitCode)): $Executable; logs: $run" }
}
foreach ($configuration in @('development', 'release')) {
    $player = "$run/$configuration/ClassicCrowd.exe"
    $buildArgs = @('-batchmode', '-quit', '-nographics', '-projectPath', $project,
        '-executeMethod', 'DK2.Unity.Editor.ClassicCrowdBuild.Build', '-probeBuild', $player,
        '-logFile', "$run/$configuration-build.log")
    if ($configuration -eq 'development') { $buildArgs += '-probeDevelopment' }
    Run-Native $Unity $buildArgs
    if (!(Test-Path $player)) { throw "No player built: $run/$configuration-build.log" }
    $playerArgs = @('-batchmode', '-screen-width', '1280', '-screen-height', '720',
        '-dk2Install', $Install, '-probeCreature', $Creature, '-probeRepresentation', $Representation, '-probeOutput', "$run/$configuration.csv", '-logFile', "$run/$configuration-player.log")
    Run-Native $player $playerArgs
    if (!(Test-Path "$run/$configuration.csv")) { throw "No completed capture: $run/$configuration-player.log" }
}
Write-Output $run
