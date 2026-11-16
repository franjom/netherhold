# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
<#
.SYNOPSIS
  Read tools/oracle/play-record.md first: every mode, output, column and trap is there.
  Play a DK2 level yourself with DK2's own packet recorder on, then keep the recording as add-only evidence.

.DESCRIPTION
  Runs DKII-DX.exe -LEVEL <Level> -PSAVE <Name> at full speed: no debugger, no memory capture, nothing
  attached to the game. DK2 writes PacketRecord\<Name>.sav itself (navigation-captures.md, Q356): every turn's
  RNG seed and state checksum, every command you give, and the camera position.

  After the game closes the script copies the recording, MESSAGE.LOG, any new in-game screenshots (SCRSHOTS)
  and a manifest into a new folder under F:\DK2Evidence\studies, makes them read-only, and decodes the packet into
  commands.tsv (one row per command) and camera.tsv (the camera per turn) for analysis.

  The run is replayable later: DKII-DX.exe -LEVEL <Level> -PLOAD <Name> 0 plays it back turn for turn, and
  tools/oracle/record_native.py can take state (with memory) over a chosen turn window of that replay, offline,
  where its slowness does not matter.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\oracle\play-record.ps1
.EXAMPLE
  # Overnight, after playing: every turn read from outside while DK2 replays undisturbed (preferred)
  powershell -ExecutionPolicy Bypass -File tools\oracle\play-record.ps1 -Replay -Study 'F:\DK2Evidence\studies\<the kept folder>' -Map 70x70
.EXAMPLE
  # Full memory at chosen turns (record_native.py; a Level 6a replay desynced at turn 76 under it, cause unsettled)
  powershell -ExecutionPolicy Bypass -File tools\oracle\play-record.ps1 -Capture -Study 'F:\DK2Evidence\studies\<the kept folder>'
  powershell -ExecutionPolicy Bypass -File tools\oracle\play-record.ps1 -Level Level6a -Name l6a-owner-01
#>
[CmdletBinding()]
param(
    [string]$Level = 'Level6a',
    [string]$Name,
    [string]$GameDir = 'E:\GogGames\Dungeon Keeper 2',
    [string]$EvidenceRoot = 'F:\DK2Evidence\studies',
    # Decode an existing recording into -Out (a new folder) without playing: commands.tsv and camera.tsv.
    [string]$Decode,
    [string]$Out,
    # Overnight: replay a kept recording (-Study) in DK2 and capture full state at chosen turns with record_native.py.
    [switch]$Capture,
    [string]$Study,
    [int[]]$Turns,
    [string]$Python = 'python',
    # Overnight, undisturbed: replay a kept recording (-Study) while dk2live.py record reads every turn from outside.
    [switch]$Replay,
    [string]$Recorder = 'D:\DungeonKeeperRemake\reverse-engineering\dk2live.py',
    # -Replay: the level's map size, WxH (Level6a 70x70), so the recorder does not latch onto the front end's world.
    [string]$Map
)
$ErrorActionPreference = 'Stop'

# Decode the packet. Layout: README play-record.md, "The packet". A 64-byte level name and a metadata dword, then one
# record per turn: count (1), stamp (4), seed (4), checksum (4), header (4), count x 18-byte commands (three dwords
# d1 d2 d3, the action id dword, a word), then a 12-byte camera (x, y in 1/4096 tile, 4 more bytes).
# Each action puts its arguments in different dwords; $ActionLayout says which, so commands.tsv gives named columns.
$ActionNames = @{
    1 = 'end'; 21 = 'leave-possession'; 25 = 'possessed-move'; 26 = 'possessed-act'; 40 = 'tag'; 41 = 'untag'
    47 = 'build-room'; 49 = 'sell'; 50 = 'place-door'; 52 = 'place-trap'; 54 = 'cast-at-place'; 55 = 'cast-on-creature'
    57 = 'pick-up-creature'; 59 = 'pick-up-gold'; 61 = 'drop'; 62 = 'slap'; 92 = 'window-focus'; 93 = 'door-lock-toggle'; 114 = 'panel-click'
}
function Get-Tile([uint32]$d) { , @([int]($d -band 0xFFFF), [int]($d -shr 16)) }
function Format-Command([uint32]$a, [uint32]$d1, [uint32]$d2, [uint32]$d3) {
    # Returns x, y, x2, y2, type, thing, wx, wy (empty where the action has none).
    $r = @('', '', '', '', '', '', '', '')
    switch ($a) {
        { $_ -in 40, 41, 49 } { $t = Get-Tile $d1; $u = Get-Tile $d2; $r[0] = $t[0]; $r[1] = $t[1]; $r[2] = $u[0]; $r[3] = $u[1] }
        47 { $r[4] = $d1; $t = Get-Tile $d2; $u = Get-Tile $d3; $r[0] = $t[0]; $r[1] = $t[1]; $r[2] = $u[0]; $r[3] = $u[1] }
        { $_ -in 50, 52 } { $t = Get-Tile $d1; $r[0] = $t[0]; $r[1] = $t[1]; $r[4] = $d2 -band 0xFFFF }
        54 { $r[4] = $d1; $r[6] = $d2 / 4096.0; $r[7] = $d3 / 4096.0 }
        55 { $r[4] = $d1; $r[5] = $d2 }
        { $_ -in 57, 59, 62, 93 } { $r[5] = $d1 }
        61 { $r[6] = $d1 / 4096.0; $r[7] = $d2 / 4096.0 }
        114 { $r[4] = "$d1/$d2" }
    }
    for ($i = 6; $i -le 7; $i++) { if ("$($r[$i])" -ne '') { $r[$i] = ([double]$r[$i]).ToString('F3', [Globalization.CultureInfo]::InvariantCulture) } }
    $r
}
function Read-Packet([string]$Path, [string]$Into) {
    $data = [IO.File]::ReadAllBytes($Path)
    if ($data.Length -lt 68) { throw 'The packet lacks its 68-byte header.' }
    $recordedLevel = [Text.Encoding]::ASCII.GetString($data, 0, 64).Split([char]0)[0]
    $commands = [Collections.Generic.List[string]]::new()
    $camera = [Collections.Generic.List[string]]::new()
    $commands.Add((@('ordinal', 'turn', 'seed', 'action', 'name', 'x', 'y', 'x2', 'y2', 'type', 'thing', 'wx', 'wy', 'd1', 'd2', 'd3', 'word') -join "`t"))
    $camera.Add("ordinal`tturn`tcommands`tcamera_x`tcamera_y")
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $offset = 68; $turns = 0; $actions = @{}; $lastStamp = 0
    while ($offset -lt $data.Length) {
        $count = $data[$offset]
        $size = 17 + $count * 18 + 12
        if ($offset + $size -gt $data.Length) { Write-Warning "Record at $offset is truncated (the game closed mid-write); stopped there."; break }
        $stamp = [BitConverter]::ToUInt32($data, $offset + 1)
        $seed = [BitConverter]::ToUInt32($data, $offset + 5)
        for ($i = 0; $i -lt $count; $i++) {
            $c = $offset + 17 + $i * 18
            $d1 = [BitConverter]::ToUInt32($data, $c); $d2 = [BitConverter]::ToUInt32($data, $c + 4)
            $d3 = [BitConverter]::ToUInt32($data, $c + 8); $action = [BitConverter]::ToUInt32($data, $c + 12)
            $word = [BitConverter]::ToUInt16($data, $c + 16)
            $name = if ($action -le 1000 -and $ActionNames.ContainsKey([int]$action)) { $ActionNames[[int]$action] } else { "action-$action" }
            $fields = @(($turns + 1), $stamp, $seed.ToString('X8'), $action, $name) + (Format-Command $action $d1 $d2 $d3) + @($d1, $d2, $d3, $word)
            $commands.Add(($fields -join "`t"))
            $actions[[string]$action] = 1 + [int]$actions[[string]$action]
        }
        $cam = $offset + 17 + $count * 18
        $camera.Add("$($turns + 1)`t" + ('{0}' -f $stamp) + "`t" + $count + "`t" + ([BitConverter]::ToInt32($data, $cam) / 4096.0).ToString('F3', $inv) + "`t" +
            ([BitConverter]::ToInt32($data, $cam + 4) / 4096.0).ToString('F3', $inv))
        $offset += $size; $turns++; $lastStamp = $stamp
    }
    [IO.File]::WriteAllLines((Join-Path $Into 'commands.tsv'), $commands)
    [IO.File]::WriteAllLines((Join-Path $Into 'camera.tsv'), $camera)
    [pscustomobject]@{ Level = $recordedLevel; Turns = $turns; Commands = $commands.Count - 1; Actions = $actions; Bytes = $data.Length; LastStamp = $lastStamp }
}

if ($Decode) {
    if (-not $Out) { throw '-Decode needs -Out, a new folder.' }
    if (Test-Path $Out) { throw "$Out exists; pick a new folder." }
    New-Item -ItemType Directory $Out | Out-Null
    $r = Read-Packet $Decode $Out
    Write-Host ("{0}: {1} turns, {2} commands -> {3}" -f $r.Level, $r.Turns, $r.Commands, $Out)
    return
}

if ($Replay) {
    # Under record_native.py a Level 6a replay went out of sync (checksum) at GT 76, while a plain -PLOAD replay ran 427
    # turns in sync (2026-11-16); why is not settled (play-record.md, Traps). dk2live.py record only reads memory from outside.
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated PowerShell (Run as administrator).' }
    if (-not $Study -or -not (Test-Path (Join-Path $Study 'manifest.json'))) { throw '-Replay needs -Study, a folder this script kept (with manifest.json).' }
    if (-not (Test-Path $Recorder)) { throw "No recorder at $Recorder." }
    if ($Map -notmatch '^\d+x\d+$') { throw '-Replay needs -Map WxH, the level''s map size (Level6a: 70x70).' }
    if (Get-Process -Name 'DKII-DX', 'DKII' -ErrorAction SilentlyContinue) { throw 'DK2 is already running; close it first.' }
    $m = Get-Content (Join-Path $Study 'manifest.json') -Raw | ConvertFrom-Json
    $exe = Join-Path $GameDir 'DKII-DX.exe'
    $packet = Join-Path $GameDir "PacketRecord\$($m.name).sav"
    if (-not (Test-Path $packet)) { throw "The recording is not in the install any more ($packet); copy it back from $Study first." }
    if ((Get-FileHash $packet -Algorithm SHA256).Hash.ToLowerInvariant() -ne $m.packet.sha256) { throw "$packet differs from the kept recording." }
    $out = "$Study--replay-" + (Get-Date -Format 'yyyyMMdd-HHmm')
    if (Test-Path $out) { throw "$out exists." }
    New-Item -ItemType Directory $out | Out-Null
    $cap = Join-Path $out 'level.dk2cap'
    # Playback consumes one record per turn (packet-playback-clock.md), so the last turn is the record count; stamps repeat
    # while the window is unfocused and end lower (Level 6a: 7243 records, last stamp 7241).
    $last = (Read-Packet $packet $out).Turns
    Write-Host "Recording the replay of $($m.name), turns up to $last, into $out" -ForegroundColor Cyan
    # The recorder first, then the game (dk2live.py record waits for the process and the level).
    $rec = Start-Process -FilePath $Python -ArgumentList @("`"$Recorder`"", 'record', "`"$cap`"", '--map', $Map, '--last-tick', $last, '--wait-game', '600') `
        -RedirectStandardOutput (Join-Path $out 'recorder.out.txt') -RedirectStandardError (Join-Path $out 'recorder.err.txt') -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    $game = Start-Process -FilePath $exe -ArgumentList "-LEVEL $($m.level) -PLOAD $($m.name) 0" -WorkingDirectory $GameDir -PassThru
    $rec.WaitForExit()
    # The owner closes replays by force; close the game this script started, then any DK2 left (a launcher hand-off).
    if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force; Start-Sleep -Seconds 2 }
    $left = @(Get-Process -Name 'DKII-DX', 'DKII' -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) { Write-Warning ("DK2 still running after the replay (pid {0}); closing it." -f ($left.Id -join ', ')); $left | Stop-Process -Force }
    # dk2live.py record ends by printing an indented JSON object (json.dumps(end, indent=1)): why it stopped, frames,
    # missed and torn ticks. Parse from the last line that is exactly '{' to the end.
    $recLines = @(Get-Content (Join-Path $out 'recorder.out.txt') -ErrorAction SilentlyContinue)
    $recEnd = $null
    for ($i = $recLines.Count - 1; $i -ge 0; $i--) {
        if ($recLines[$i] -eq '{') { try { $recEnd = ($recLines[$i..($recLines.Count - 1)] -join "`n") | ConvertFrom-Json } catch { $recEnd = $null }; break }
    }
    $log = Join-Path $GameDir 'MESSAGE.LOG'
    if (Test-Path $log) { Copy-Item $log (Join-Path $out 'MESSAGE.replay.LOG') }
    $sync = if (Test-Path $log) { Select-String -Path $log -Pattern 'Out Of Sync' -SimpleMatch | Select-Object -First 1 } else { $null }
    $summary = [ordered]@{ study = $Study; packet = $packet; map = $Map; lastTick = $last; recorderExit = $rec.ExitCode; recorderEnd = $recEnd; capture = $cap
        captureBytes = if (Test-Path $cap) { (Get-Item $cap).Length } else { 0 }; desync = [string]$sync; finished = (Get-Date).ToString('o') }
    $summary | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $out 'replay-summary.json')
    if ($rec.ExitCode -ne 0 -or -not (Test-Path $cap)) { Write-Warning "The recorder failed (exit $($rec.ExitCode)); see recorder.err.txt. This run is not evidence." }
    elseif ($sync) { Write-Warning "The replay went out of sync: $sync" } else { Write-Host 'No out-of-sync line in the game log.' -ForegroundColor Green }
    if ($recEnd) { Write-Host ("Recorder: " + ($recEnd | ConvertTo-Json -Compress -Depth 4)) } else { Write-Warning 'The recorder printed no end summary.' }
    Write-Host "Done: $out (recorder exit $($rec.ExitCode))."
    return
}

if ($Capture) {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated PowerShell (Run as administrator).' }
    if (-not $Study -or -not (Test-Path (Join-Path $Study 'manifest.json'))) { throw '-Capture needs -Study, a folder this script kept (with manifest.json).' }
    $m = Get-Content (Join-Path $Study 'manifest.json') -Raw | ConvertFrom-Json
    $exe = Join-Path $GameDir 'DKII-DX.exe'
    $packet = Join-Path $GameDir "PacketRecord\$($m.name).sav"
    if (-not (Test-Path $packet)) { throw "The recording is not in the install any more ($packet); copy it back from $Study first." }
    if ((Get-FileHash $packet -Algorithm SHA256).Hash.ToLowerInvariant() -ne $m.packet.sha256) { throw "$packet differs from the kept recording." }
    $recorder = Join-Path $PSScriptRoot 'record_native.py'
    $data = [IO.File]::ReadAllBytes($packet)
    # The first turn record's seed: record_native.py sets the world seed at turn 1, and the replay must run on the game's own.
    $seed = [BitConverter]::ToUInt32($data, 68 + 5)
    if (-not $Turns) {
        # Default: one turn per burst of hand work, at its first pick-up, so every creature taken is alive in some frame.
        # record_native.py counts record ordinals, so take them from a fresh decode (the kept study may predate the column).
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ('play-record-' + [guid]::NewGuid())
        New-Item -ItemType Directory $tmp | Out-Null
        [void](Read-Packet $packet $tmp)
        $picks = Import-Csv (Join-Path $tmp 'commands.tsv') -Delimiter "`t" | Where-Object action -eq '57' | ForEach-Object { [int]$_.ordinal }
        Remove-Item -Recurse -Force $tmp
        $Turns = @(); $last = -10000
        foreach ($t in ($picks | Sort-Object -Unique)) { if ($t - $last -ge 400) { $Turns += $t; $last = $t } }
    }
    $Turns = @($Turns | Where-Object { $_ -ge 3 -and $_ -lt $m.turns } | Sort-Object -Unique)
    Write-Warning 'A Level 6a replay went out of sync at GT 76 under record_native.py while a plain replay did not (cause unsettled). Prefer -Replay for long replays (play-record.md, Traps).'
    Write-Host ("Capturing turns {0} of {1} (seed 0x{2:X8}). Each run replays from turn 1 at game speed." -f ($Turns -join ', '), $m.name, $seed) -ForegroundColor Cyan

    function Invoke-Capture([int]$Turn, [string]$Into) {
        & $Python -I $recorder --exe $exe --packet $packet --seed ('0x{0:X8}' -f $seed) --first $Turn --last $Turn --out $Into | Out-Host
        $log = Join-Path $GameDir 'MESSAGE.LOG'
        $sync = if (Test-Path $log) { Select-String -Path $log -Pattern 'Gone Out Of Sync' -SimpleMatch | Select-Object -First 1 } else { $null }
        if (Test-Path $log) { Copy-Item $log (Join-Path $Into 'MESSAGE.replay.LOG') -ErrorAction SilentlyContinue }
        [pscustomobject]@{ Turn = $Turn; Out = $Into; Exit = $LASTEXITCODE; Verified = (Test-Path (Join-Path $Into 'verified.json')); Desync = [string]$sync }
    }

    # Smoke first: three turns. A desync means the replay is not the game that was played; stop rather than capture it.
    $smoke = Invoke-Capture 3 ("$Study--capture-smoke-" + (Get-Date -Format 'HHmm'))
    if ($smoke.Desync) { throw "The replay went out of sync ($($smoke.Desync)); nothing more captured. See $($smoke.Out)." }
    if (-not $smoke.Verified) { throw "The smoke capture did not verify (exit $($smoke.Exit)); see $($smoke.Out)." }
    $results = @($smoke)
    foreach ($t in $Turns) {
        $r = Invoke-Capture $t ("$Study--capture-t$t")
        $results += $r
        Write-Host ("turn {0}: verified={1} desync={2}" -f $t, $r.Verified, [bool]$r.Desync)
        if ($r.Desync) { Write-Warning 'Out of sync; later turns would be too. Stopping.'; break }
    }
    $summaryPath = "$Study--captures-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json'
    if (Test-Path $summaryPath) { throw "$summaryPath exists." }
    $results | ConvertTo-Json | Set-Content -Encoding utf8 $summaryPath
    Write-Host 'Done.' -ForegroundColor Green
    return
}

# DK2 runs elevated (TRAPS.md); waiting on an elevated game needs an elevated shell.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated PowerShell (Run as administrator).' }

# DK2 truncates long names; keep it to letters, digits, '-' and '_' (the packet header holds 64 bytes).
if (-not $Name) { $Name = ($Level.ToLowerInvariant() -replace '^level', 'l') + '-owner-' + (Get-Date -Format 'yyyyMMdd-HHmm') }
if ($Name -notmatch '^[A-Za-z0-9_-]{1,40}$') { throw "Name '$Name' must be 1-40 letters, digits, '-' or '_'." }
if ($Level -notmatch '^[A-Za-z0-9_]+$') { throw "Level '$Level' is not a level name." }

$exe = Join-Path $GameDir 'DKII-DX.exe'   # not DKII.EXE: its -LEVEL exits before the level loads (TRAPS.md)
$packetDir = Join-Path $GameDir 'PacketRecord'
$packet = Join-Path $packetDir "$Name.sav"
$study = Join-Path $EvidenceRoot ("$Name--" + (Get-Date -Format 'yyyy-MM-dd') + '--001')
$mapFile = Join-Path $GameDir "Data\editor\maps\$Level.kwd"

if (-not (Test-Path $exe)) { throw "No game at $exe." }
if (-not (Test-Path $mapFile)) { throw "No level $Level ($mapFile)." }
if (Get-Process -Name 'DKII-DX', 'DKII' -ErrorAction SilentlyContinue) { throw 'DK2 is already running; close it first.' }
if (Test-Path $packet) { throw "$packet exists; evidence is never overwritten. Pick another -Name." }
if (Test-Path $study) { throw "$study exists; evidence is never overwritten. Pick another -Name." }
if (-not (Test-Path $EvidenceRoot)) { throw "No evidence root $EvidenceRoot." }
New-Item -ItemType Directory -Force $packetDir | Out-Null

$shotDir = Join-Path $GameDir 'SCRSHOTS'
$shotsBefore = @(if (Test-Path $shotDir) { Get-ChildItem $shotDir -File | ForEach-Object FullName })
$started = Get-Date
$arguments = "-LEVEL $Level -PSAVE $Name"

Write-Host "Starting DK2: $Level, recording to $packet" -ForegroundColor Cyan
Write-Host 'Play normally. Win, lose or quit to Windows; the script continues once the game has closed.'
Write-Host '(Any in-game screenshots saved to SCRSHOTS during the run are kept with it.)'
$game = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $GameDir -PassThru
$game.WaitForExit()
$ended = Get-Date
Start-Sleep -Seconds 2   # let DK2 finish writing its files
if (-not (Test-Path $packet)) { throw "DK2 wrote no $packet (exit code $($game.ExitCode)). Nothing to keep." }

New-Item -ItemType Directory $study | Out-Null
Copy-Item $packet (Join-Path $study "$Name.sav")
$log = Join-Path $GameDir 'MESSAGE.LOG'
if (Test-Path $log) { Copy-Item $log (Join-Path $study 'MESSAGE.LOG') }
$shots = @(if (Test-Path $shotDir) { Get-ChildItem $shotDir -File | Where-Object { $shotsBefore -notcontains $_.FullName } })
if ($shots.Count -gt 0) {
    $shotOut = New-Item -ItemType Directory (Join-Path $study 'screenshots')
    $shots | Copy-Item -Destination $shotOut
}

$decoded = Read-Packet $packet $study
$turns = $decoded.Turns; $actions = $decoded.Actions; $recordedLevel = $decoded.Level

$hash = { param($p) (Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest = [ordered]@{
    kind = 'owner-play-packet-recording'
    level = $Level
    recordedLevel = $recordedLevel
    name = $Name
    arguments = $arguments
    started = $started.ToString('o')
    ended = $ended.ToString('o')
    wallSeconds = [math]::Round(($ended - $started).TotalSeconds, 1)
    exitCode = $game.ExitCode
    turns = $turns
    commands = $decoded.Commands
    actionsById = $actions.GetEnumerator() | Sort-Object Name | ForEach-Object { [ordered]@{ action = $_.Name; count = $_.Value } }
    exe = [ordered]@{ path = $exe; sha256 = (& $hash $exe) }
    packet = [ordered]@{ source = $packet; sha256 = (& $hash $packet); bytes = $decoded.Bytes }
    replay = "& '$exe' -LEVEL $Level -PLOAD $Name 0   (run from '$GameDir', elevated)"
    note = 'No memory was captured during play. State for any turn window comes later from a -PLOAD replay (record_native.py).'
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $study 'manifest.json')
$sums = Get-ChildItem $study -File -Recurse | ForEach-Object { '{0}  {1}' -f (& $hash $_.FullName), $_.FullName.Substring($study.Length + 1) }
[IO.File]::WriteAllLines((Join-Path $study 'sha256.txt'), $sums)
Get-ChildItem $study -File -Recurse | ForEach-Object { $_.IsReadOnly = $true }

Write-Host ''
Write-Host "Kept: $study" -ForegroundColor Green
Write-Host ("  {0} turns, {1} commands, {2:N0} s at the keyboard" -f $turns, $decoded.Commands, ($ended - $started).TotalSeconds)
Write-Host "  The packet also stays in $packetDir for replay."
