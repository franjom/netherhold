// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// The input journal of one adapter session (ADR-0553 Phase 1), enabled by an explicit output folder.
/// Each session (the front end, each level)
/// gets a new folder (<see cref="FolderName"/>) with <see cref="FramesFile"/>, one <see cref="FrameInputRecord"/> per
/// rendered frame, and, once its simulation ticks, <see cref="TicksFile"/> (ADR-0552: frame per tick, hash after it).
/// The host calls, per frame: <see cref="BeginFrame"/>, any <see cref="Intent"/>, at most one <see cref="HandOff"/>
/// just before it clears its queue, <see cref="Ticks"/>, then <see cref="EndFrame"/>. Recording only reads, and a
/// journal that fails stops itself, never the game. The frame calls are main-thread; the tick calls may be on a tick
/// thread, and <see cref="Close"/> runs on the main thread after that thread is joined.
/// </summary>
public sealed class FrameJournalSession
{
    public const string FramesFile = "frames.journal";
    public const string TicksFile = "input.journal";
    /// <summary>The header key naming the table that defines the <see cref="HostKey"/> values.</summary>
    public const string KeyTableKey = "keys";
    /// <summary>The key table used in older recording fixtures.</summary>
    public const string UnityKeyTable = "UnityEngine.KeyCode";
    public const string GodotKeyTable = "Godot.Key";

    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly FrameInputRecorder _recorder = new();
    private string? _folder;
    private IReadOnlyDictionary<string, string>? _header;
    private FileStream? _framesFile;
    private FrameInputJournalWriter? _frames;
    private long _framesWritten;
    // Opened and written on the tick thread; closed on the main thread only after the host joins it.
    private FileStream? _ticksFile;
    private InputJournalWriter? _ticks;
    private volatile bool _ticksFailed;

    public FrameJournalSession(Action<string> log, Action<string> warn)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    /// <summary>Whether <see cref="Open"/> has run; it runs once, on the session's first frame.</summary>
    public bool Resolved { get; private set; }

    /// <summary>Whether the frame journal is open; the host builds a frame's raw sample only then.</summary>
    public bool Recording
    {
        get
        {
            return _frames != null;
        }
    }

    /// <summary>The session's folder, once opened.</summary>
    public string? Folder
    {
        get
        {
            return _folder;
        }
    }

    /// <summary><c>session-yyyyMMdd-HHmmss-fff-front-end</c>, or the level's name in place of <c>front-end</c>.</summary>
    public static string FolderName(DateTime utcNow, GameSessionMode mode, string levelName)
    {
        return $"session-{utcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{(mode == GameSessionMode.FrontEnd ? "front-end" : levelName)}";
    }

    /// <summary>The shared journal header. The host adds its engine version and any known commit.</summary>
    public static Dictionary<string, string> Header(string source, string keyTable, GameSessionMode mode, string level,
        ulong seed, PlayerId viewer, bool fogOfWar, bool tickOnWorker, int screenWidth, int screenHeight)
    {
        return new()
        {
            ["source"] = source,
            [KeyTableKey] = keyTable,
            ["session.mode"] = mode.ToString(),
            ["level"] = level,
            ["seed"] = seed.ToString(CultureInfo.InvariantCulture),
            ["viewing.player"] = viewer.Value.ToString(CultureInfo.InvariantCulture),
            ["fog.of.war"] = fogOfWar ? "on" : "off",
            ["tick.on.worker"] = tickOnWorker ? "on" : "off",
            ["screen"] = $"{screenWidth}x{screenHeight}",
            ["commit"] = "unknown",
            ["src.dirty"] = "unknown",
        };
    }

    /// <summary>Resolves the journal once: with a <paramref name="root"/>, creates the session folder under it and opens
    /// the frame journal with the header <paramref name="header"/> builds. A failure warns and leaves the journal off.</summary>
    public void Open(string? root, string folderName, Func<IReadOnlyDictionary<string, string>> header)
    {
        if (header is null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        Resolved = true;
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            _folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(_folder);
            _header = header();
            _framesFile = new FileStream(Path.Combine(_folder, FramesFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _frames = new FrameInputJournalWriter(_framesFile, _header);
            _log($"Input journal: {_folder}");
        }
        catch (Exception ex)
        {
            StopFrames(ex);
        }
    }

    /// <summary>Starts a frame from the host's raw sample and its queue; nothing while the journal is off.</summary>
    public void BeginFrame(FrameInputRecord raw, IReadOnlyList<PlayerCommand> pending)
    {
        if (_frames != null)
        {
            _recorder.BeginFrame(raw, pending);
        }
    }

    public void Intent(in PlayerIntent intent)
    {
        if (_frames != null)
        {
            _recorder.Intent(intent);
        }
    }

    /// <summary>Called just before the pending queue is handed to a tick and cleared.</summary>
    public void HandOff(IReadOnlyList<PlayerCommand> pending)
    {
        if (_frames != null)
        {
            _recorder.HandOff(pending);
        }
    }

    /// <summary>The frame's first tick and its tick count.</summary>
    public void Ticks(GameTick start, int ticks)
    {
        if (_frames != null)
        {
            _recorder.Ticks(start, ticks);
        }
    }

    /// <summary>Ends the frame and writes its record (Unity's <c>LateUpdate</c>).</summary>
    public void EndFrame(IReadOnlyList<PlayerCommand> pending, double tickDebt)
    {
        if (_frames == null)
        {
            return;
        }

        Write(_recorder.EndFrame(pending, tickDebt));
    }

    /// <summary>On the tick thread, before the tick that starts at <paramref name="tick"/>: opens the tick journal on the
    /// first one (its header is the frame journal's and the start tick, then the starting hash) and writes the frame.</summary>
    public void BeforeTick(long tick, in InputFrame input, GameSimulation simulation)
    {
        if (_frames == null || _ticksFailed)
        {
            return;
        }

        try
        {
            if (_ticks == null)
            {
                OpenTicks(simulation);
            }

            _ticks!.WriteFrame(tick, input);
        }
        catch (Exception ex)
        {
            StopTicks(ex);
        }
    }

    /// <summary>On the tick thread: the hash once <paramref name="tick"/> ticks have run.</summary>
    public void AfterTick(long tick, GameSimulation simulation)
    {
        if (_frames == null || _ticksFailed)
        {
            return;
        }

        try
        {
            _ticks!.WriteHash(tick, simulation.StateHash());
        }
        catch (Exception ex)
        {
            StopTicks(ex);
        }
    }

    /// <summary>On the tick thread: a tick threw; the tick journal ends with the failure and stops.</summary>
    public void TickFailed(Exception failure, GameSimulation simulation)
    {
        if (failure is null)
        {
            throw new ArgumentNullException(nameof(failure));
        }

        if (_ticks == null || _ticksFailed)
        {
            return;
        }

        try
        {
            long tick = simulation.World.CurrentTick.Value;
            _ticks.WriteEnd(tick, $"exception at tick {tick}: {failure.GetType().FullName}: {failure.Message}");
        }
        catch (Exception ex)
        {
            _warn($"Input journal (ticks) could not record the failure: {ex.Message}");
        }
        _ticksFailed = true;
    }

    /// <summary>Main thread, after the tick thread is joined: writes the frame still open (a session switch leaves from
    /// inside the frame, before its end), ends both journals and closes the files.</summary>
    public void Close(string reason, IReadOnlyList<PlayerCommand> pending, double tickDebt, GameSimulation? simulation)
    {
        if (_frames != null)
        {
            Write(_recorder.EndFrame(pending, tickDebt));
        }

        Close(ref _ticks, ref _ticksFile, w =>
        {
            if (!_ticksFailed && simulation != null)
            {
                w.WriteEnd(simulation.World.CurrentTick.Value, reason);
            }
        });
        Close(ref _frames, ref _framesFile, w => w.WriteEnd(_framesWritten, reason));
    }

    private void OpenTicks(GameSimulation simulation)
    {
        var header = new Dictionary<string, string>(_header!)
        {
            ["world.start.tick"] = simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture),
        };
        _ticksFile = new FileStream(Path.Combine(_folder!, TicksFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _ticks = new InputJournalWriter(_ticksFile, header);
        _ticks.WriteHash(simulation.World.CurrentTick.Value, simulation.StateHash());
    }

    private void Write(FrameInputRecord? frame)
    {
        if (frame == null || _frames == null)
        {
            return;
        }

        try
        {
            _frames.Write(frame);
            _framesWritten++;
        }
        catch (Exception ex)
        {
            StopFrames(ex);
        }
    }

    private void StopTicks(Exception ex)
    {
        _ticksFailed = true;
        _warn($"Input journal (ticks) stopped: {ex.Message}");
    }

    /// <summary>Main thread: a frame-journal failure closes the frame journal now; the tick journal may be in use on the
    /// tick thread, so it only stops writing and is closed later.</summary>
    private void StopFrames(Exception ex)
    {
        _warn($"Input journal stopped: {ex.Message}");
        _ticksFailed = true;
        Close(ref _frames, ref _framesFile, w => w.WriteEnd(_framesWritten, "journal failed: " + ex.Message));
    }

    private void Close<T>(ref T? writer, ref FileStream? file, Action<T> end) where T : class, IDisposable
    {
        try
        {
            if (writer != null)
            {
                end(writer);
                writer.Dispose();
            }
        }
        catch (Exception ex)
        {
            _warn($"Input journal did not close cleanly: {ex.Message}");
        }
        try { file?.Dispose(); }
        catch (IOException) { }
        writer = null;
        file = null;
    }
}
