// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Which loop a journaled frame belongs to.</summary>
public enum FrameInputMode : byte
{
    Game,
    FrontEnd,
}

/// <summary>The HUD state that decides whether ordinary input reaches the world.</summary>
[Flags]
public enum FrameHudFlags : byte
{
    None = 0,
    OrdinaryInput = 1,
    Modal = 2,
    Options = 4,
    CameraPath = 8,
}

/// <summary>Mouse buttons as the host numbers them: 0 primary, 1 secondary, 2 middle.</summary>
[Flags]
public enum FrameMouseButtons : byte
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Middle = 4,
}

/// <summary>A key as the host engine numbers it (Unity's <c>KeyCode</c> today; the journal header
/// names the numbering). Phase 2 of ADR-0553 replaces it with an engine-neutral key.</summary>
public readonly record struct HostKey(int Code);

/// <summary>When a command entered the queue: before this frame's update (a UI Toolkit callback
/// between frames); during the frame and in time for its tick hand-off (or in a frame with none);
/// or after the hand-off, so it waits for a later frame's tick.</summary>
public enum CommandQueuedWhen : byte
{
    BeforeUpdate,
    DuringFrame,
    AfterHandOff,
}

public readonly record struct QueuedCommand(CommandQueuedWhen When, PlayerCommand Command);

/// <summary>
/// One rendered frame of the Unity input journal (ADR-0553 Phase 1): the raw device state the
/// adapter polled, the HUD state it read, the intents it built, the commands it queued in order,
/// and the ticks the frame ran. <see cref="TickAtStart"/> is null for a frame that returned before
/// the tick loop. <see cref="CommandsHandedToTick"/> counts the whole queue given to the frame's
/// first tick, earlier frames' commands included.
/// </summary>
public sealed record FrameInputRecord
{
    public long Frame { get; init; }
    public double UnscaledDeltaSeconds { get; init; }
    public FrameInputMode Mode { get; init; }
    public GameTick? TickAtStart { get; init; }
    public int TicksRun { get; init; }
    public int CommandsHandedToTick { get; init; }
    public double TickDebtAfter { get; init; }
    public float PointerX { get; init; }
    public float PointerY { get; init; }
    public FrameMouseButtons ButtonsHeld { get; init; }
    public FrameMouseButtons ButtonsDown { get; init; }
    public FrameMouseButtons ButtonsUp { get; init; }
    public float WheelX { get; init; }
    public float WheelY { get; init; }
    public float PanHorizontal { get; init; }
    public float PanVertical { get; init; }
    public string TypedText { get; init; } = "";
    public IReadOnlyList<HostKey> KeysDown { get; init; } = [];
    public IReadOnlyList<HostKey> KeysUp { get; init; } = [];
    public IReadOnlyList<HostKey> KeysHeld { get; init; } = [];
    public FrameHudFlags Hud { get; init; }
    public PlayerToolMode ActiveTool { get; init; }
    public IReadOnlyList<PlayerIntent> Intents { get; init; } = [];
    public IReadOnlyList<QueuedCommand> Commands { get; init; } = [];
}

/// <summary>
/// Writes the per-frame Unity input journal. Each record is encoded whole and length-prefixed, so
/// <see cref="FrameInputJournal.Read"/> keeps every whole record of a killed session. A frame
/// with an intent or a command is flushed at once, others at least every
/// <see cref="FlushEveryFrames"/> frames.
/// </summary>
public sealed class FrameInputJournalWriter : IDisposable
{
    public const int FlushEveryFrames = 60;

    private readonly Stream _stream;
    private readonly BinaryWriter _w;
    private readonly MemoryStream _buffer = new();
    private readonly BinaryWriter _b;
    private int _unflushed;

    public FrameInputJournalWriter(Stream stream, IReadOnlyDictionary<string, string> header)
    {
        _stream = stream;
        _w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        _b = new BinaryWriter(_buffer, Encoding.UTF8, leaveOpen: true);
        _w.Write(FrameInputJournal.Magic);
        _w.Write(FrameInputJournal.FormatVersion);
        _w.Write(header.Count);
        foreach ((string key, string value) in header.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            _w.Write(key);
            _w.Write(value);
        }
        Flush();
    }

    public void Write(FrameInputRecord frame)
    {
        _buffer.SetLength(0);
        FrameInputJournal.Encode(_b, frame);
        _b.Flush();
        _w.Write(FrameInputJournal.FrameRecord);
        _w.Write((int)_buffer.Length);
        _w.Write(_buffer.GetBuffer(), 0, (int)_buffer.Length);
        if (frame.Intents.Count > 0 || frame.Commands.Count > 0 || ++_unflushed >= FlushEveryFrames) Flush();
    }

    /// <summary>Closes the journal with how the session ended.</summary>
    public void WriteEnd(long frames, string reason)
    {
        _w.Write(FrameInputJournal.EndRecord);
        _w.Write(frames);
        _w.Write(reason);
        Flush();
    }

    private void Flush()
    {
        _w.Flush();
        _stream.Flush();
        _unflushed = 0;
    }

    public void Dispose()
    {
        _w.Flush();
        _w.Dispose();
        _b.Dispose();
        _buffer.Dispose();
    }
}

/// <summary>A frame input journal read back: its header, its whole frames, and how it ended.</summary>
public sealed class FrameInputJournal
{
    internal const uint Magic = 0x46324B44; // "DK2F"
    internal const int FormatVersion = 1;
    internal const byte FrameRecord = 1;
    internal const byte EndRecord = 3;

    private FrameInputJournal(IReadOnlyDictionary<string, string> header, IReadOnlyList<FrameInputRecord> frames,
        long? framesWritten, string? endReason)
    {
        Header = header;
        Frames = frames;
        FramesWritten = framesWritten;
        EndReason = endReason;
    }

    public IReadOnlyDictionary<string, string> Header { get; }
    public IReadOnlyList<FrameInputRecord> Frames { get; }

    /// <summary>From the end record; null when the journal was cut short.</summary>
    public long? FramesWritten { get; }
    public string? EndReason { get; }
    public bool Truncated => FramesWritten is null;

    /// <summary>Reads every whole record. Fails on a wrong magic or format version, a cut-off
    /// header and an unknown record or command; a record cut off by the end of the stream ends the read.</summary>
    public static FrameInputJournal Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a frame input journal");
            int format = r.ReadInt32();
            if (format != FormatVersion) throw new InvalidDataException($"frame input journal format {format}, expected {FormatVersion}");
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++) header[r.ReadString()] = r.ReadString();
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("the frame input journal ends inside its header");
        }

        var frames = new List<FrameInputRecord>();
        while (true)
        {
            int tag = stream.ReadByte();
            if (tag < 0) return new FrameInputJournal(header, frames, null, null);
            try
            {
                switch (tag)
                {
                    case FrameRecord:
                        int length = r.ReadInt32();
                        byte[] payload = r.ReadBytes(length);
                        if (payload.Length < length) return new FrameInputJournal(header, frames, null, null);
                        using (var record = new BinaryReader(new MemoryStream(payload), Encoding.UTF8))
                            frames.Add(Decode(record));
                        break;
                    case EndRecord:
                        long written = r.ReadInt64();
                        return new FrameInputJournal(header, frames, written, r.ReadString());
                    default:
                        throw new InvalidDataException($"unknown frame input journal record {tag}");
                }
            }
            catch (EndOfStreamException)
            {
                return new FrameInputJournal(header, frames, null, null);
            }
        }
    }

    internal static void Encode(BinaryWriter w, FrameInputRecord f)
    {
        w.Write(f.Frame);
        w.Write(f.UnscaledDeltaSeconds);
        w.Write((byte)f.Mode);
        w.Write(f.TickAtStart.HasValue);
        if (f.TickAtStart is GameTick tick) w.Write(tick.Value);
        w.Write(f.TicksRun);
        w.Write(f.CommandsHandedToTick);
        w.Write(f.TickDebtAfter);
        w.Write(f.PointerX);
        w.Write(f.PointerY);
        w.Write((byte)f.ButtonsHeld);
        w.Write((byte)f.ButtonsDown);
        w.Write((byte)f.ButtonsUp);
        w.Write(f.WheelX);
        w.Write(f.WheelY);
        w.Write(f.PanHorizontal);
        w.Write(f.PanVertical);
        w.Write(f.TypedText);
        Keys(w, f.KeysDown);
        Keys(w, f.KeysUp);
        Keys(w, f.KeysHeld);
        w.Write((byte)f.Hud);
        w.Write((byte)f.ActiveTool);
        w.Write(f.Intents.Count);
        foreach (PlayerIntent intent in f.Intents) Intent(w, intent);
        w.Write(f.Commands.Count);
        foreach (QueuedCommand queued in f.Commands)
        {
            w.Write((byte)queued.When);
            CommandCodec.Write(w, queued.Command);
        }
    }

    private static FrameInputRecord Decode(BinaryReader r) => new()
    {
        Frame = r.ReadInt64(),
        UnscaledDeltaSeconds = r.ReadDouble(),
        Mode = (FrameInputMode)r.ReadByte(),
        TickAtStart = r.ReadBoolean() ? new GameTick(r.ReadInt64()) : null,
        TicksRun = r.ReadInt32(),
        CommandsHandedToTick = r.ReadInt32(),
        TickDebtAfter = r.ReadDouble(),
        PointerX = r.ReadSingle(),
        PointerY = r.ReadSingle(),
        ButtonsHeld = (FrameMouseButtons)r.ReadByte(),
        ButtonsDown = (FrameMouseButtons)r.ReadByte(),
        ButtonsUp = (FrameMouseButtons)r.ReadByte(),
        WheelX = r.ReadSingle(),
        WheelY = r.ReadSingle(),
        PanHorizontal = r.ReadSingle(),
        PanVertical = r.ReadSingle(),
        TypedText = r.ReadString(),
        KeysDown = ReadKeys(r),
        KeysUp = ReadKeys(r),
        KeysHeld = ReadKeys(r),
        Hud = (FrameHudFlags)r.ReadByte(),
        ActiveTool = (PlayerToolMode)r.ReadByte(),
        Intents = Repeat(r, ReadIntent),
        Commands = Repeat(r, b => new QueuedCommand((CommandQueuedWhen)b.ReadByte(), CommandCodec.Read(b))),
    };

    private static void Keys(BinaryWriter w, IReadOnlyList<HostKey> keys)
    {
        w.Write(keys.Count);
        foreach (HostKey key in keys) w.Write(key.Code);
    }

    private static HostKey[] ReadKeys(BinaryReader r) => Repeat(r, b => new HostKey(b.ReadInt32()));

    private static T[] Repeat<T>(BinaryReader r, Func<BinaryReader, T> read)
    {
        int count = r.ReadInt32();
        if (count < 0) throw new InvalidDataException($"negative count {count} in a frame record");
        var items = new T[count];
        for (int i = 0; i < count; i++) items[i] = read(r);
        return items;
    }

    private static void Intent(BinaryWriter w, PlayerIntent i)
    {
        w.Write(i.PlayerId.Value);
        w.Write((byte)i.Tool);
        w.Write(i.PointerTile.HasValue);
        if (i.PointerTile is TilePos tile) { w.Write(tile.X); w.Write(tile.Y); }
        w.Write((byte)i.PrimaryButton);
        w.Write((byte)i.RoomId);
        w.Write((byte)i.SecondaryButton);
        w.Write(i.PointerOverHud);
        w.Write(i.PointerPosition.HasValue);
        if (i.PointerPosition is FpPosition2 p) { w.Write(p.X.Raw); w.Write(p.Y.Raw); }
    }

    private static PlayerIntent ReadIntent(BinaryReader r)
    {
        var player = new PlayerId(r.ReadByte());
        var tool = (PlayerToolMode)r.ReadByte();
        TilePos? tile = r.ReadBoolean() ? new TilePos(r.ReadInt32(), r.ReadInt32()) : null;
        var primary = (PointerButtonTransition)r.ReadByte();
        var room = (RoomTypeId)r.ReadByte();
        var secondary = (PointerButtonTransition)r.ReadByte();
        bool overHud = r.ReadBoolean();
        FpPosition2? position = r.ReadBoolean() ? new FpPosition2(new Fp32(r.ReadInt32()), new Fp32(r.ReadInt32())) : null;
        return new PlayerIntent(player, tool, tile, primary, room, secondary, overHud, position);
    }
}
