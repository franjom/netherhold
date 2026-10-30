// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>One row of the reward table at <c>0x0066C780</c> (52 rows of 16 bytes): the item's id,
/// its internal name, an icon word and the Speech.str line that announces it.</summary>
public readonly record struct RewardItem(byte Id, string Name, ushort Icon, uint SpeechTextId);

/// <summary>The in-level win screen (interface <c>0x006CEA10</c>, campaign-win-sequence.md §5): the
/// next level's rewards announced one at a time, then the 500 ms continue rule.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/campaign-win-sequence.md")]
public sealed class WinScreen
{
    /// <summary>The table at <c>0x0066C780</c>, read 2026-10-26, indexed by id.</summary>
    public static readonly IReadOnlyList<RewardItem> Items = new RewardItem[]
    {
        new(0, "None", 0, 0), new(1, "Alarm", 295, 1369), new(2, "Barricade", 314, 1366), new(3, "Boulder", 294, 1377),
        new(4, "Braced", 306, 1373), new(5, "Bridge_Stone", 242, 1370), new(6, "Bridge_Wooden", 236, 1360),
        new(7, "Call_To_Arms", 283, 1368), new(8, "Casino", 239, 1371), new(9, "Chicken", 292, 1392),
        new(10, "Create_Gold", 290, 1385), new(11, "Create_Imp", 284, 1352), new(12, "Dark_Library", 232, 1350),
        new(13, "Earthquake", 287, 1374), new(14, "Fear", 299, 1363), new(15, "Fireburst", 296, 1390),
        new(16, "Freeze", 298, 1381), new(17, "Gas", 293, 1372), new(18, "Graveyard", 240, 1379),
        new(19, "Guard_Room", 238, 1358), new(20, "Guard_Post", 0, 1362), new(21, "Hatchery", 230, 1394),
        new(22, "Heal", 289, 1365), new(23, "Inferno", 288, 1389), new(24, "Lair", 229, 1395),
        new(25, "Lightning", 297, 1386), new(26, "Magic_Door", 310, 1383), new(27, "Pit", 235, 1384),
        new(28, "Possess", 282, 1359), new(29, "Prison", 234, 1361), new(30, "Reaper_Talisman04", 0, 0),
        new(31, "Fight", 0, 0), new(32, "Secret_Door", 312, 1387), new(33, "Sentry", 301, 1356),
        new(34, "Sight_Of_Evil", 281, 1367), new(35, "Spike", 302, 1375), new(36, "Steel_Door", 308, 1378),
        new(37, "Summon_Reaper", 285, 1382), new(38, "<Not Used>", 0, 0), new(39, "Temple", 241, 1388),
        new(40, "Thunderbolt", 286, 1353), new(41, "Torture", 237, 1364), new(42, "Training", 231, 1351),
        new(43, "Treasury", 228, 1354), new(44, "Trigger", 300, 1376), new(45, "Turncoat", 291, 1380),
        new(46, "<Not Used>", 0, 0), new(47, "Wood_Door", 304, 1357), new(48, "Workshop", 233, 1355),
        new(49, "Reaper_Talisman01", 0, 0), new(50, "Reaper_Talisman02", 0, 0), new(51, "Reaper_Talisman03", 0, 0),
    };

    /// <summary>The deadline the done screen re-arms while no continue or leave is asked (<c>0x0042C390</c>).</summary>
    public const double ContinueDelayMilliseconds = 500;
    /// <summary>DK2 steps the tablet once per drawn frame (<c>0x0042C390</c> → <c>0x0042F040</c>); the
    /// remake fixes the rate, a stand-in for a frame rate nothing records.</summary>
    public const double TabletFramesPerSecond = 30;
    private const double TabletFrameMilliseconds = 1000 / TabletFramesPerSecond;

    public event Action<uint>? SpeechRequested;
    private readonly byte[] _rewards;
    private readonly Func<uint, string?> _speech;
    private int _step;
    private double _speechEnds;
    private double _deadline;
    private double _nextTabletFrame;
    private readonly Vector3[] _tabletPoints;
    private readonly Random _random;

    /// <param name="rewards">World <c>+0x9B86..+0x9B8A</c>: the level's four NextRewards and byte 25606.</param>
    /// <param name="speech">Speech.str text for an id, used for subtitles with speech disabled.</param>
    /// <param name="tabletPoints">The Tablet mesh's control points, where its sparkles start.</param>
    public WinScreen(byte[] rewards, Func<uint, string?> speech, Vector3[]? tabletPoints = null, Random? random = null)
    {
        _rewards = rewards;
        _speech = speech;
        _tabletPoints = tabletPoints ?? [];
        _random = random ?? new Random();
    }

    /// <summary>The reward bytes a level announces (<c>0x0051E6D0</c>): NextRewards, then byte 25606
    /// (<c>LevelDef.Unknown_6403[3]</c>) when the extended metadata is present.</summary>
    public static byte[] RewardsOf(LevelDef level)
    {
        var bytes = new byte[5];
        level.NextRewards.Span[..Math.Min(4, level.NextRewards.Length)].CopyTo(bytes);
        if (level.Unknown_6403.Length > 3) bytes[4] = level.Unknown_6403.Span[3];
        return bytes;
    }

    public bool Started { get; private set; }
    /// <summary><c>+0x4CEA</c>: every reward announced.</summary>
    public bool Done { get; private set; }
    /// <summary>The item on screen, or null.</summary>
    public RewardItem? Current { get; private set; }
    /// <summary>The item's tablet (<c>+0x4C6E</c>), drawn while the screen is not done.</summary>
    public RewardTablet Tablet { get; } = new();
    /// <summary>The announcement's text, or null.</summary>
    public string? Text => Current is { } item ? _speech(item.SpeechTextId) : null;

    /// <summary>Slot <c>+0x40</c> (<c>0x0042BE40</c>): the screen starts on its first step.</summary>
    public void Start(double now)
    {
        Started = true;
        Done = false;
        _step = 0;
        _deadline = 0;
        Show(now);
    }

    /// <summary><c>0x0042BE90(i)</c>: byte i, zero or the fifth ending the screen.</summary>
    private void Show(double now)
    {
        byte id = _step < _rewards.Length ? _rewards[_step] : (byte)0;
        if (_step >= 5 || id == 0 || id >= Items.Count)
        {
            Current = null;
            Done = true;
            return;
        }
        Current = Items[id];
        if (Items[id].SpeechTextId != 0) SpeechRequested?.Invoke(Items[id].SpeechTextId);
        Tablet.Restart();
        _nextTabletFrame = now;
        // With speech audio off the line lasts as a subtitle does (SubtitleQueue).
        _speechEnds = now + (Text?.Length ?? 0) * SubtitleQueue.MillisecondsPerCharacter;
    }

    /// <summary><c>0x0042C390</c>, per frame: nothing while the camera is on a path; the tablet's frames;
    /// the next step once both the line and the tablet have finished; when done, every 500 ms, a
    /// continue the player asked for is taken.</summary>
    /// <returns>True on the frame the continue (action 134) is due.</returns>
    public bool Update(double now, bool cameraOnPath, bool continueRequested, bool? speechPlaying = null)
    {
        if (!Started || cameraOnPath) return false;
        if (!Done)
        {
            // More than four frames late, the clock restarts; up to four late, the missed frames are drawn.
            if (now - _nextTabletFrame > 4 * TabletFrameMilliseconds) _nextTabletFrame = now;
            for (; now >= _nextTabletFrame; _nextTabletFrame += TabletFrameMilliseconds)
                Tablet.DrawFrame(now, _tabletPoints, _random);
            if ((speechPlaying ?? now < _speechEnds) || !Tablet.Done) return false;
            _step++;
            Show(now);
            return false;
        }
        if (_deadline == 0) _deadline = now + ContinueDelayMilliseconds;
        if (now < _deadline) return false;
        _deadline = now + ContinueDelayMilliseconds;
        return continueRequested;
    }

    /// <summary>Taken away by the continuation.</summary>
    public void Close()
    {
        Started = false;
        Current = null;
    }
}
