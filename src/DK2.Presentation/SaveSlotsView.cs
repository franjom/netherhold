// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Save;

namespace DK2.Presentation;

/// <summary>The named-file browser of Options pages23/24 and front-end page8; no fixed slot count.</summary>
public sealed class SaveSlotsView
{
    private readonly Func<IReadOnlyList<SavedSessionEntry>> _list;
    public SaveSlotsView(string directory, Func<IReadOnlyList<SavedSessionEntry>>? list = null)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _list = list ?? (() => SavedSessionFile.List(Directory));
        Refresh();
    }
    public string Directory { get; }
    public IReadOnlyList<SavedSessionEntry> Entries { get; private set; } = Array.Empty<SavedSessionEntry>();
    public int SelectedIndex { get; private set; } = -1;
    public SavedSessionEntry? Selected => SelectedIndex >= 0 && SelectedIndex < Entries.Count ? Entries[SelectedIndex] : null;
    public string Name { get; private set; } = "";
    public int Revision { get; private set; }
    public void Refresh()
    {
        string? path = Selected?.Path;
        Entries = _list();
        SelectedIndex = -1;
        for (int i = 0; i < Entries.Count; i++) if (Entries[i].Path == path) SelectedIndex = i;
        Revision++;
    }
    public bool Select(int index)
    {
        if ((uint)index >= (uint)Entries.Count) return false;
        SelectedIndex = index; Name = Entries[index].DisplayName; Revision++;
        return true;
    }
    public void SetName(string value)
    {
        Name = value.Length > 128 ? value.Substring(0, 128) : value;
        Revision++;
    }
}
