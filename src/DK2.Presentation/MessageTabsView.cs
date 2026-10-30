// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Media;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>One tab on menu39 this frame: the message, its graphic family (1..6), the frame
/// (0 plain, 1 hovered, 2 flashing) and its local x in menu units, slide included.</summary>
public readonly record struct MessageTab(uint MessageId, int Graphic, int Frame, int LocalX);

/// <summary>The message tabs of the bottom bar (menu39) and the message panel they open (menu40),
/// docs/research/message-tabs.md. The opened and dismissed bits are the interface's own (record
/// <c>+0x2A</c> bits 8 and 4, written with no packet), so they live here, keyed by message id,
/// never in the simulation.</summary>
public sealed class MessageTabsView
{
    /// <summary>The updater looks at only this many of the player's records (0x00423BD0).</summary>
    public const int RecordsExamined = 8;
    public const int SlotWidth = 88;
    /// <summary>The slide-in lasts 64 steps of 10 ms from the record's stamp, 16 units a step.</summary>
    private const int SlideSteps = 64, SlideStart = 1020, SlideStep = 16;

    private readonly PlayerId _viewer;
    private readonly HashSet<uint> _dismissed = [], _opened = [];
    private readonly Dictionary<uint, long> _stamps = [];
    private readonly List<MessageTab> _tabs = [];
    private readonly List<uint> _gone = [];
    private readonly Dictionary<uint, PlayerMessage> _live = [];
    private readonly Dictionary<uint, int> _references = [];
    private readonly GameDatabase? _database;

    /// <param name="database">Gives types 17 and 18 their creature's speech; without it they
    /// take their table row, which is empty.</param>
    public MessageTabsView(PlayerId viewer, GameDatabase? database = null)
    {
        _viewer = viewer;
        _database = database;
    }

    public SoundRequests Sounds { get; } = new();
    public SoundRequests Speech { get; } = new();
    private readonly HashSet<uint> _heard = new();
    private readonly HashSet<uint> _landed = new();
    public IReadOnlyList<MessageTab> Tabs => _tabs;
    /// <summary>The message menu40 shows, or null when it is closed.</summary>
    public PlayerMessage? Open { get; private set; }
    /// <summary>A slot index the pointer is over, or -1; set by the host before <see cref="Capture"/>.</summary>
    public int Hovered { get; set; } = -1;

    /// <summary>0x00423BD0 each frame: the first eight of the viewer's records, oldest first, give a
    /// tab while NeedsTab is set and the interface has not dismissed them. Slot n sits at
    /// (n + 1) * 88 (Review Objective is at 0; the Ally button, mode 3 only, is not built).</summary>
    public void Capture(WorldState world, long nowMilliseconds)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        _tabs.Clear();
        _live.Clear();
        bool flashTurn = ((world.CurrentTick.Value + 1) & 1) != 0;   // DK2's turn is our tick plus one (ADR-0119)
        int examined = 0;
        foreach (PlayerMessage message in world.Messages.Entries)
        {
            if (message.PlayerId != _viewer) continue;
            _live[message.Id] = message;
            if (_heard.Add(message.Id) && message.Kind is not (PlayerMessageKind.ScriptedSpeech or PlayerMessageKind.CreatureIntroduction))
            {
                int reference = ReferenceAtPost(world, message);
                if (reference < -10000) Speech.Add(new SoundRequest(SoundCategory.Mentor, new SoundId((uint)(-reference - 9999))));
            }
            if (examined++ >= RecordsExamined) continue;
            if (!message.NeedsTab || _dismissed.Contains(message.Id)) continue;
            bool arrived = !_stamps.TryGetValue(message.Id, out long stamp);
            if (arrived) _stamps[message.Id] = stamp = nowMilliseconds;
            if (!_references.ContainsKey(message.Id)) _references[message.Id] = ReferenceAtPost(world, message);
            int slot = _tabs.Count;
            long age = (nowMilliseconds - stamp) / 10;
            int x = (slot + 1) * SlotWidth + (age < SlideSteps ? SlideStart - SlideStep * (int)age : 0);
            if (age < SlideSteps || _landed.Add(message.Id))
                Sounds.Add(new SoundRequest(new SoundCategory("gui_message_bar"),
                    new SoundId(age >= SlideSteps ? 0x322u : arrived ? 0x320u : 0x321u), Voice: new SoundVoiceId((uint)slot + 1)));
            int frame = slot == Hovered ? 1 : flashTurn && !_opened.Contains(message.Id) ? 2 : 0;
            _tabs.Add(new MessageTab(message.Id, MessageTypeTable.Graphic((int)message.Kind), frame, x));
        }
        _gone.Clear();
        foreach (uint id in _stamps.Keys) if (!_live.ContainsKey(id)) _gone.Add(id);
        foreach (uint id in _gone) { _stamps.Remove(id); _landed.Remove(id); _dismissed.Remove(id); _opened.Remove(id); _references.Remove(id); }
        if (Open is PlayerMessage open && !_live.ContainsKey(open.Id)) Open = null;
    }

    /// <summary>A record of the viewer's seen at the last capture, or null.</summary>
    public PlayerMessage? MessageById(uint id) => _live.TryGetValue(id, out PlayerMessage message) ? message : null;

    /// <summary>0x00423F50, a left click: the message is opened (its flash stops) and menu40 shows it.</summary>
    public void OpenTab(int slot)
    {
        if ((uint)slot >= (uint)_tabs.Count || !_live.TryGetValue(_tabs[slot].MessageId, out PlayerMessage message)) return;
        _opened.Add(message.Id);
        Open = message;
    }

    /// <summary>0x00424220, a right click: the tab goes, and menu40 closes if it shows that message.</summary>
    public void DismissTab(int slot)
    {
        if ((uint)slot >= (uint)_tabs.Count) return;
        uint id = _tabs[slot].MessageId;
        _dismissed.Add(id);
        if (Open?.Id == id) Open = null;
    }

    /// <summary>menu40's exit button (0x00424790): dismiss the open message and close.</summary>
    public void DismissOpen()
    {
        if (Open is PlayerMessage open) _dismissed.Add(open.Id);
        Open = null;
    }

    /// <summary>menu40's tick button: close only.</summary>
    public void CloseOpen() => Open = null;

    /// <summary>The message's string reference (record <c>+0x0F</c>): its type's row, or for a type
    /// with a variant list the variant its Data1 names.</summary>
    public static int StringReference(PlayerMessage message) => message.Kind switch
    {
        PlayerMessageKind.ScriptedSpeech => unchecked(-message.Data1),
        PlayerMessageKind.CreatureIntroduction => unchecked(-message.Data2),
        _ => MessageTypeTable.StringReference((int)message.Kind, message.Data1),
    };

    /// <summary>The reference a tabbed message was given when first seen, as DK2 fixes it at post
    /// (<c>0x004C4600</c>); else <see cref="StringReference(PlayerMessage)"/>.</summary>
    public int Reference(PlayerMessage message) =>
        _references.TryGetValue(message.Id, out int reference) ? reference : StringReference(message);

    /// <summary>Where menu40's goto button sends the view (<c>0x004C4BE0</c>, then <c>0x004C3DC0</c>), in
    /// tiles of DK2's plane, or null when the button hides (<c>0x00424660</c>). By DK2 type: a tile in
    /// Data1/Data2 (1, 3, 11, 12, 29, 38, 47), the tile's centre; a live creature in Data2 (4, 5, 17,
    /// 18, 27) or Data1 (7, 8, 13, 14, 19, 57, 58, 60, 64, 72, 73, 78, 79, 81), its position; a room
    /// (32 to 35, 42, 44, 45 with Data1 below 0xFF; 25, 61, 66), its centre (<c>+0x27</c>, ours
    /// <see cref="RoomInstance.CentreFieldX"/>, the box middle: STRONGLY INFERRED, Q515), Data2 being
    /// ours the room's first slab; 42 carries none in either game (<see cref="PlayerMessageQueue.PostRoom"/>),
    /// so it never shows the button. DK2 also checks a thing's kind (<c>+0x12</c> 0), which ours does not.
    /// Not built: types 22, 26 and 48 (a kind-2 thing), 36 and 37 (a tile packed in Data2) and 85 to 87
    /// (the Heart), none of which our queue posts.</summary>
    public (float X, float Y)? Location(WorldState world, in PlayerMessage message)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        switch ((int)message.Kind)
        {
            case 1: case 3: case 11: case 12: case 29: case 38: case 47:
                return message.Data1 == -1 || message.Data2 == -1 ? null : (message.Data1 + .5f, message.Data2 + .5f);
            case 4: case 5: case 17: case 18: case 27:
                return CreatureAt(world, message.Data2);
            case 7: case 8: case 13: case 14: case 19: case 57: case 58: case 60: case 64: case 72: case 73:
            case 78: case 79: case 81:
                return CreatureAt(world, message.Data1);
            case 32: case 33: case 34: case 35: case 42: case 44: case 45:
                return message.Data1 < 0xFF ? RoomAt(world, message.Data2) : null;
            case 25: case 61: case 66:
                return RoomAt(world, message.Data2);
            default:
                return null;
        }
    }

    private static (float, float)? CreatureAt(WorldState world, int id) =>
        world.CreatureById(id) is CreatureState creature
            ? (creature.PosX / (float)DK2.Core.Fp32.One, creature.PosY / (float)DK2.Core.Fp32.One) : null;

    private (float, float)? RoomAt(WorldState world, int firstSlab)
    {
        if (_database is null || (uint)firstSlab >= (uint)(world.Width * world.Height)) return null;
        return RoomIndex.RoomAt(world, _database, firstSlab) is RoomInstance room
            ? (room.CentreFieldX + .5f, room.CentreFieldY + .5f) : null;
    }

    /// <summary><c>0x004C4600</c>'s types 17 and 18: <c>-9999 - (base + 1 + complaint)</c>, base the
    /// creature definition's unhappy or angry speech (<c>def+0x6CD</c>/<c>+0x6CF</c>), so Speech
    /// <c>base + complaint</c>. Data1 is the complaint, Data2 the creature; a creature already gone
    /// keeps the row's reference.</summary>
    private int ReferenceAtPost(WorldState world, in PlayerMessage message)
    {
        if (_database is null || message.Kind is not (PlayerMessageKind.CreatureUnhappy or PlayerMessageKind.CreatureAngry))
            return StringReference(message);
        if (world.CreatureById((int)message.Data2) is not CreatureState creature
            || !_database.Creatures.TryGetValue(creature.CreatureTypeId, out CreatureDef? definition))
            return StringReference(message);
        int speech = message.Kind == PlayerMessageKind.CreatureUnhappy ? definition.UnhappySpeechId : definition.AngrySpeechId;
        return -9999 - (speech + 1 + (int)message.Data1);
    }
}
