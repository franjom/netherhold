// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
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

    public MessageTabsView(PlayerId viewer) => _viewer = viewer;

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
            if (examined++ >= RecordsExamined) continue;
            if (!message.NeedsTab || _dismissed.Contains(message.Id)) continue;
            if (!_stamps.TryGetValue(message.Id, out long stamp)) _stamps[message.Id] = stamp = nowMilliseconds;
            int slot = _tabs.Count;
            long age = (nowMilliseconds - stamp) / 10;
            int x = (slot + 1) * SlotWidth + (age < SlideSteps ? SlideStart - SlideStep * (int)age : 0);
            int frame = slot == Hovered ? 1 : flashTurn && !_opened.Contains(message.Id) ? 2 : 0;
            _tabs.Add(new MessageTab(message.Id, MessageTypeTable.Graphic((int)message.Kind), frame, x));
        }
        _gone.Clear();
        foreach (uint id in _stamps.Keys) if (!_live.ContainsKey(id)) _gone.Add(id);
        foreach (uint id in _gone) { _stamps.Remove(id); _dismissed.Remove(id); _opened.Remove(id); }
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
    public static int StringReference(PlayerMessage message) =>
        MessageTypeTable.StringReference((int)message.Kind, message.Data1);
}
