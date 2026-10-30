// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

public enum MusicLevel : uint { Silent = 255, Raid, Battle, LargeBattle, Reaper }
public readonly record struct MusicFrame(int MusicIntensity, int AmbienceIntensity);

[Dk2Behavior(Confidence.Confirmed, "docs/research/music.md#8-pseudocode-ours")]
public sealed class MusicDirector
{
    private int _musicIntensity;
    public MusicLevel Level { get; private set; } = MusicLevel.Silent;
    public int Intensity => Of(Level);
    public void Set(MusicLevel level) => Level = level;
    public void Raise(MusicLevel level) { if ((uint)level > (uint)Level) Level = level; }
    public static int Of(MusicLevel level) => level switch
    { MusicLevel.Raid => 20, MusicLevel.Battle => 40, MusicLevel.LargeBattle => 60, MusicLevel.Reaper => 80, _ => 0 };
    public MusicFrame AdvanceFrame(bool musicEnabled, int soundQuality)
    {
        if (musicEnabled)
        {
            _musicIntensity = Intensity;
            if (Level > MusicLevel.Raid) Level = MusicLevel.Raid;
        }
        return new MusicFrame(_musicIntensity, Intensity + (soundQuality == 1 ? 20 : 0));
    }
}

/// <summary>The linked player's inclusive-range picker; item and link indices on disk are1-based.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/music.md#6-the-segment-picker-0x006163c0-linked-player-vtable-0x00674180")]
public sealed class MusicSegmentPicker(SoundGroup group, Func<uint> random)
{
    private int? _current;
    private int _intensity;
    public bool Stopped { get; private set; }
    public void SetIntensity(int intensity)
    {
        _intensity = intensity;
        if (Stopped) { Stopped = false; _current = null; }
    }
    public SoundItem? Pick()
    {
        if (Stopped || group.Items.Count == 0) return null;
        _current ??= 0;
        SoundItem item = group.Items[_current.Value];
        if (item.Links.Count == 0) { _current = null; return null; }
        int count = 0;
        foreach (SoundLink link in item.Links) if (link.Minimum <= _intensity && _intensity <= link.Maximum) count++;
        if (count == 0) { _intensity = 127; Stopped = true; return null; }
        uint choice = random() % (uint)count;
        foreach (SoundLink link in item.Links)
            if (link.Minimum <= _intensity && _intensity <= link.Maximum && choice-- == 0)
            {
                if (link.Target == 0 || link.Target > group.Items.Count) throw new InvalidDataException("Music link target is outside its group.");
                _current = (int)link.Target - 1;
                return group.Items[_current.Value];
            }
        throw new InvalidDataException("Music link count is inconsistent.");
    }
}
