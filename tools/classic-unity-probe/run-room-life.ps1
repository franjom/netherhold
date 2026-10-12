param(
    [string]$SourceRepo = 'D:/DungeonKeeperRemake-classic',
    [string]$Unity = 'C:/Program Files/Unity 6000.6.0f1/Editor/Unity.exe',
    [string]$Install = 'E:/GogGames/Dungeon Keeper 2',
    [string]$OutputRoot = 'D:/DungeonKeeperRemake/build/room-life-probe',
    [string]$Project = 'D:/DungeonKeeperRemake/build/classic-unity-probe/20261011-182558/project'
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath "$Project/.classic-crowd-probe") -and !(Test-Path -LiteralPath "$Project/.room-life-probe")) { throw 'Marked scratch project required' }
$run = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $run -Force | Out-Null
Set-Content -LiteralPath "$Project/.room-life-probe" -Value 'isolated room life verification project'
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath "$SourceRepo/unity/DK2.Unity/$folder" -Destination $Project -Recurse -Force
}
foreach ($relative in @('Assets/DK2/ClassicCrowdProbe.cs', 'Assets/DK2/ClassicCrowdAlternative.cs', 'Assets/DK2/Editor/ClassicCrowdBuild.cs')) {
    $target = Join-Path $Project $relative
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
}
Copy-Item -LiteralPath "$PSScriptRoot/RoomLifeProbe.cs" -Destination "$Project/Assets/DK2/RoomLifeProbe.cs" -Force
Copy-Item -LiteralPath "$PSScriptRoot/RoomLifeBuild.cs" -Destination "$Project/Assets/DK2/Editor/RoomLifeBuild.cs" -Force
$manifestPath = "$Project/Packages/manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (!$manifest.dependencies.'com.unity.modules.particlesystem') { throw 'Production particle system module missing' }
$manifest.dependencies | Add-Member -NotePropertyName 'com.unity.modules.imageconversion' -NotePropertyValue '1.0.0' -Force
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 50), (New-Object Text.UTF8Encoding($false)))
$assemblyPath = "$Project/Assets/DK2/DK2.Unity.asmdef"
$assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
$assembly.references = @(@($assembly.references) + @('Unity.RenderPipelines.Universal.Runtime') | Select-Object -Unique)
[IO.File]::WriteAllText($assemblyPath, ($assembly | ConvertTo-Json -Depth 50), (New-Object Text.UTF8Encoding($false)))
git -c "safe.directory=$SourceRepo" -C $SourceRepo rev-parse HEAD | Set-Content -LiteralPath "$run/commit.txt"
Set-Content -LiteralPath "$run/project-path.txt" -Value $Project
Get-ChildItem -LiteralPath "$Project/Assets/Plugins/DK2" -Filter '*.dll' | Get-FileHash -Algorithm SHA256 | Export-Csv "$run/assembly-hashes.csv" -NoTypeInformation
Get-ChildItem -LiteralPath "$Project/Assets/DK2" -Recurse -File | Where-Object { $_.Extension -in '.cs', '.shader' } | Get-FileHash -Algorithm SHA256 | Export-Csv "$run/source-hashes.csv" -NoTypeInformation
function Run-Native([string]$Executable, [string[]]$Arguments) {
    $quoted = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) { throw 'Embedded quotes in process argument' }
        '"' + $argument + '"'
    }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddMinutes(10)
    while (!$process.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) { $process.Kill(); throw "Timed out: $Executable; $run" }
    }
    if ($process.ExitCode -ne 0) { throw "Probe failed ($($process.ExitCode)): $Executable; $run" }
}
$player = "$run/player/RoomLife.exe"
Run-Native $Unity @('-batchmode', '-quit', '-nographics', '-projectPath', $Project, '-executeMethod', 'DK2.Unity.Editor.RoomLifeBuild.Build', '-probeBuild', $player, '-logFile', "$run/build.log")
if (!(Test-Path -LiteralPath $player)) { throw "No built player: $run" }
Run-Native $player @('-batchmode', '-screen-width', '1280', '-screen-height', '720', '-dk2Install', $Install, '-probeOutput', $run, '-logFile', "$run/player.log")
if (!(Test-Path -LiteralPath "$run/metrics.json")) { throw "No metrics: $run" }
Write-Output $run
