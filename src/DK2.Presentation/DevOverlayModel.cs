// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>How a developer overlay row is drawn: a group's heading, a labelled value, or the key hint at the foot.</summary>
public enum DevOverlayRowKind : byte
{
    Heading,
    Row,
    Hint,
}

/// <summary>Which scene the overlay describes.</summary>
public enum DevOverlayScene : byte
{
    FrontEnd,
    InGame,
}

/// <summary>What the adapter reads for the overlay at a refresh: the engine's monitors and the scene's state. Strings are
/// references the adapter already holds, so filling this allocates nothing.</summary>
public struct DevOverlayFacts
{
    public DevOverlayScene Scene;
    /// <summary>The front end's page (<see cref="DevOverlayScene.FrontEnd"/>).</summary>
    public FrontEndPage Page;
    /// <summary>Whether <see cref="GpuMilliseconds"/> was measured this frame; the engine reports it only when asked.</summary>
    public bool GpuMeasured;
    public int DrawCalls, Objects;
    public double VideoMemoryMegabytes;
    public string? Level;
    public GameTick Turn;
    public int GameSpeed;
    public bool Paused, CameraPath;
    /// <summary>The settled world and its data, for the hovered tile; null outside a level.</summary>
    public WorldState? World;
    public GameDatabase? Database;
    /// <summary>The tile under the pointer, null when the pointer is over the HUD or off the map.</summary>
    public TilePos? HoverTile;
}

/// <summary>The developer overlay's contents (F3, or <c>--dev-overlay</c>): grouped rows of plain-word labels and values.
/// The adapter feeds each frame's times (<see cref="AddPart"/>, <see cref="AddTurns"/>, <see cref="EndFrame"/>) and, about
/// four times a second when <see cref="EndFrame"/> says so, the scene's facts (<see cref="Compose"/>); it then draws
/// <see cref="Label"/> and <see cref="Value"/> for each row. Values are written into reused character buffers, so a
/// running overlay allocates nothing once each player and page name has been seen. Ours, not DK2's.</summary>
public sealed class DevOverlayModel
{
    /// <summary>How often the text is rebuilt.</summary>
    public const double RefreshSeconds = 0.25;
    /// <summary>The span the averages, the worst frame and the rates cover.</summary>
    public const double WindowSeconds = 1.0;
    /// <summary>Toggles the overlay. DK2's own default binds F3 and F4 to camera views (Oblique, User Camera 1,
    /// <c>DefaultKeyBindings</c>), which the remake does not wire yet; a port of those must move these keys.</summary>
    public const string ToggleKey = "F3";
    /// <summary>Computes the state hash, which costs too much to do every frame.</summary>
    public const string HashKey = "F4";

    public const int MaxRows = 24;
    public const int ValueCapacity = 72;

    private readonly DevOverlayRowKind[] _kinds = new DevOverlayRowKind[MaxRows];
    private readonly string[] _labels = new string[MaxRows];
    private readonly char[][] _values = new char[MaxRows][];
    private readonly int[] _lengths = new int[MaxRows];
    private readonly string?[] _playerNames = new string?[256];
    private readonly Dictionary<FrontEndPage, string> _pageNames = new();
    private Func<byte, string>? _playerName;
    private int _count;

    private TimeWindow _current, _shown;
    private bool _haveShown;
    private double _lastFrameMs, _lastGpuMs, _sinceRefresh = RefreshSeconds;
    private bool _hashKnown;
    private ulong _hash;
    private GameTick _hashTurn;

    /// <summary>A window's sums.</summary>
    private struct TimeWindow
    {
        public int Frames, Turns, GpuFrames;
        public double Seconds, WorstMs, GpuMs, TurnMs, WorldMs, HudMs, OtherMs;
    }

    public DevOverlayModel()
    {
        for (int i = 0; i < MaxRows; i++)
        {
            _values[i] = new char[ValueCapacity];
            _labels[i] = "";
        }
    }

    public int RowCount => _count;

    public DevOverlayRowKind Kind(int row) => _kinds[row];

    public string Label(int row) => _labels[row];

    public ReadOnlySpan<char> Value(int row) => new(_values[row], 0, _lengths[row]);

    /// <summary>Whether a state hash is shown (<see cref="SetHash"/>).</summary>
    public bool HashKnown => _hashKnown;

    /// <summary>The level's player names (<see cref="PlayerNames.From"/>), each asked once and kept.</summary>
    public void UsePlayerNames(Func<byte, string> playerName)
    {
        _playerName = playerName ?? throw new ArgumentNullException(nameof(playerName));
        Array.Clear(_playerNames, 0, _playerNames.Length);
    }

    /// <summary>Adds a timed part of this frame's CPU work: the present, animation and lights are the world's draw, the
    /// HUD part the HUD's, the rest (sound, script, input, the wait) other work.</summary>
    public void AddPart(FrameTelemetryPart part, double milliseconds)
    {
        switch (part)
        {
            case FrameTelemetryPart.Present:
            case FrameTelemetryPart.Animation:
            case FrameTelemetryPart.Lights:
                _current.WorldMs += milliseconds;
                break;
            case FrameTelemetryPart.Hud:
                _current.HudMs += milliseconds;
                break;
            default:
                _current.OtherMs += milliseconds;
                break;
        }
    }

    /// <summary>Adds the simulation turns this frame ran and their time.</summary>
    public void AddTurns(int turns, double milliseconds)
    {
        _current.Turns += turns;
        _current.TurnMs += milliseconds;
    }

    /// <summary>Ends a frame of <paramref name="frameSeconds"/>, with the GPU's time for it when measured (negative when not).
    /// Returns whether the text is due for a rebuild (<see cref="RefreshSeconds"/>).</summary>
    public bool EndFrame(double frameSeconds, double gpuMilliseconds)
    {
        double ms = frameSeconds * 1000.0;
        _lastFrameMs = ms;
        _current.Frames++;
        _current.Seconds += frameSeconds;
        if (ms > _current.WorstMs) _current.WorstMs = ms;
        if (gpuMilliseconds >= 0)
        {
            _lastGpuMs = gpuMilliseconds;
            _current.GpuMs += gpuMilliseconds;
            _current.GpuFrames++;
        }
        if (_current.Seconds >= WindowSeconds)
        {
            _shown = _current;
            _haveShown = true;
            _current = default;
        }
        _sinceRefresh += frameSeconds;
        if (_sinceRefresh < RefreshSeconds) return false;
        _sinceRefresh = 0;
        return true;
    }

    /// <summary>Shows <paramref name="hash"/>, the simulation's state hash at <paramref name="turn"/>.</summary>
    public void SetHash(ulong hash, GameTick turn)
    {
        _hash = hash;
        _hashTurn = turn;
        _hashKnown = true;
    }

    /// <summary>Forgets the hash, as when the session it was computed for ends.</summary>
    public void ClearHash() => _hashKnown = false;

    /// <summary>Rebuilds every row from <paramref name="facts"/> and the last full window's times (the running window's
    /// before the first is full).</summary>
    public void Compose(in DevOverlayFacts facts)
    {
        _count = 0;
        TimeWindow w = _haveShown ? _shown : _current;
        double perFrame = w.Frames > 0 ? 1.0 / w.Frames : 0;

        Heading("Performance");
        Row("Frames per second").Fixed(w.Seconds > 0 ? w.Frames / w.Seconds : 0, 0);
        Row("Frame time").Fixed(_lastFrameMs, 1).Text(" ms now, ").Fixed(w.Seconds * 1000.0 * perFrame, 1)
            .Text(" average, ").Fixed(w.WorstMs, 1).Text(" worst");
        if (facts.GpuMeasured && w.GpuFrames > 0)
            Row("GPU time").Fixed(_lastGpuMs, 2).Text(" ms now, ").Fixed(w.GpuMs / w.GpuFrames, 2).Text(" average");
        else Row("GPU time").Text("not measured");
        if (facts.Scene == DevOverlayScene.InGame)
            Row("CPU time").Text("turns ").Fixed(w.TurnMs * perFrame, 2).Text(", world ").Fixed(w.WorldMs * perFrame, 2)
                .Text(", HUD ").Fixed(w.HudMs * perFrame, 2).Text(", other ").Fixed(w.OtherMs * perFrame, 2).Text(" ms");
        Row("Draw calls").Integer(facts.DrawCalls).Text(", ").Integer(facts.Objects).Text(" objects, ")
            .Fixed(facts.VideoMemoryMegabytes, 0).Text(" MB video memory");

        if (facts.Scene == DevOverlayScene.FrontEnd)
        {
            Heading("Front end");
            Row("Page").Text(PageName(facts.Page));
            Hint("F3 hides this panel.");
            return;
        }

        Heading("Game");
        Row("Level").Text(facts.Level ?? "");
        Row("Turn").Integer(facts.Turn.Value);
        RowBuilder speed = Row("Game speed").Integer(facts.GameSpeed);
        if (facts.Paused) speed.Text(", paused");
        Row("Turns per second").Fixed(w.Seconds > 0 ? w.Turns / w.Seconds : 0, 1).Text(" of ")
            .Fixed(TargetTurnsPerSecond(facts.GameSpeed, facts.Paused), 1).Text(" wanted");
        Row("Camera path").Text(facts.CameraPath ? "playing" : "none");

        Heading("Hovered tile");
        if (facts.HoverTile is TilePos tile && facts.World is WorldState world && facts.Database is GameDatabase database
            && world.Contains(tile.X, tile.Y))
        {
            ref TileState state = ref world.Tile(tile);
            TerrainDef terrain = database.TerrainById(state.TerrainId);
            Row("Tile").Integer(tile.X).Text(", ").Integer(tile.Y);
            Row("Terrain").Text(terrain.Name);
            RowBuilder owner = Row("Owner");
            if (state.OwnerPlayerId == PlayerIds.Unowned) owner.Text("nobody");
            else owner.Text(PlayerName(state.OwnerPlayerId)).Text(" (player ").Integer(state.OwnerPlayerId).Text(")");
            Row("Room").Text(terrain.IsRoom && database.RoomsByTerrain.TryGetValue(terrain.TerrainId, out RoomDef? room)
                ? room.Name : "none");
        }
        else Row("Tile").Text("none (the pointer is over the HUD or off the map)");

        Heading("Determinism");
        if (_hashKnown) Row("State hash").Hex(_hash).Text(" at turn ").Integer(_hashTurn.Value);
        else Row("State hash").Text("press F4 to compute it");
        Hint("F3 hides this panel. F4 computes the state hash.");
    }

    /// <summary>The turns a second the pacer aims for: <see cref="SimulationTiming.TicksPerSecond"/> at game speed 4, in
    /// proportion to the setting (<see cref="TickPacer.Accrue"/>), none while paused.</summary>
    public static double TargetTurnsPerSecond(int gameSpeed, bool paused) =>
        paused ? 0 : SimulationTiming.TicksPerSecond * (gameSpeed / 4.0);

    private string PlayerName(byte id) =>
        _playerNames[id] ??= _playerName?.Invoke(id) ?? "Player";

    private string PageName(FrontEndPage page)
    {
        if (_pageNames.TryGetValue(page, out string? name)) return name;
        name = page == FrontEndPage.None ? "starting" : page.ToString();
        _pageNames[page] = name;
        return name;
    }

    private void Heading(string text) => Start(DevOverlayRowKind.Heading, text);

    private void Hint(string text) => Start(DevOverlayRowKind.Hint, text);

    private RowBuilder Row(string label) => Start(DevOverlayRowKind.Row, label);

    private RowBuilder Start(DevOverlayRowKind kind, string label)
    {
        if (_count == MaxRows) return new RowBuilder(this, -1);
        int row = _count++;
        _kinds[row] = kind;
        _labels[row] = label;
        _lengths[row] = 0;
        return new RowBuilder(this, row);
    }

    /// <summary>Writes one row's value; past <see cref="ValueCapacity"/> the rest is dropped.</summary>
    private readonly struct RowBuilder
    {
        private readonly DevOverlayModel _model;
        private readonly int _row;

        public RowBuilder(DevOverlayModel model, int row)
        {
            _model = model;
            _row = row;
        }

        public RowBuilder Text(string text)
        {
            foreach (char c in text) Put(c);
            return this;
        }

        public RowBuilder Integer(long value)
        {
            if (value < 0)
            {
                Put('-');
                // The magnitude as unsigned, so long.MinValue negates without overflow.
                return Digits(unchecked((ulong)-(value + 1)) + 1);
            }
            return Digits((ulong)value);
        }

        /// <summary><paramref name="value"/> rounded to <paramref name="decimals"/> places (0 to 3); not a number shows a dash.</summary>
        public RowBuilder Fixed(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { Put('-'); return this; }
            if (value < 0) { Put('-'); value = -value; }
            ulong scale = decimals switch { <= 0 => 1, 1 => 10, 2 => 100, _ => 1000 };
            ulong scaled = (ulong)Math.Min(Math.Round(value * scale, MidpointRounding.AwayFromZero), 1e15);
            Digits(scaled / scale);
            if (scale == 1) return this;
            Put('.');
            ulong fraction = scaled % scale;
            for (ulong digit = scale / 10; digit > 0; digit /= 10)
            {
                Put((char)('0' + (int)(fraction / digit)));
                fraction %= digit;
            }
            return this;
        }

        public RowBuilder Hex(ulong value)
        {
            for (int shift = 60; shift >= 0; shift -= 4)
            {
                int nibble = (int)((value >> shift) & 0xF);
                Put((char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10));
            }
            return this;
        }

        private RowBuilder Digits(ulong value)
        {
            ulong divisor = 1;
            while (value / divisor >= 10) divisor *= 10;
            for (; divisor > 0; divisor /= 10)
            {
                Put((char)('0' + (int)(value / divisor)));
                value %= divisor;
            }
            return this;
        }

        private void Put(char c)
        {
            if (_row < 0) return;
            ref int length = ref _model._lengths[_row];
            if (length == ValueCapacity) return;
            _model._values[_row][length++] = c;
        }
    }
}
