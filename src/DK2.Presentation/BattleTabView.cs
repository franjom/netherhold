// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The three buttons of the Battle tab, menu 43: the crossed blades (widget 1), the Call
/// to Arms cancel (widget 2) and the alarm (widget 3).</summary>
public enum BattleTabButton : byte { None = 0, Battle = 1, CallToArms = 2, Alarm = 3 }

/// <summary>What a press on one of menu 43's buttons asks of the runner (<see cref="BattleTabView.Action"/>).</summary>
public enum BattleTabAction : byte
{
    None = 0,
    /// <summary>The blades' right click, the F key's zoom (<c>0x00426960</c>): the next battle, through <see cref="BattleNavigation"/>.</summary>
    ZoomToBattle,
    /// <summary>The cancel's left click (<c>0x004268A0</c>): a <c>RemoveCallToArmsCommand</c>, game action 0x81.</summary>
    CancelCallToArms,
    /// <summary>The cancel's right click (<c>0x00426830</c>): a glide to <see cref="BattleTabView.CallToArmsTile"/>.</summary>
    GoToCallToArms,
    /// <summary>The alarm's right click (<c>0x00426A00</c>): a glide to <see cref="ThingNavigation.NextAlarm"/>.</summary>
    NextAlarm,
}

/// <summary>What a press on menu 43 asks of the adapter (<see cref="BattleTabView.Click"/>): a HUD action to apply, a
/// command to post or a tile to glide to; at most one is set, and none when the press does nothing.</summary>
public readonly record struct BattleTabClick(ClassicHudAction HudAction, PlayerCommand? Command, TilePos? Glide);

/// <summary>Which of menu 43's buttons show this frame; each frame callback sets its widget's state
/// on its own and the positions are static, so a hidden button leaves its gap.</summary>
public readonly record struct BattleTabSnapshot(bool Battle, bool CallToArms, bool Alarm)
{
    public bool Shows(BattleTabButton button)
    {
        return button switch
        {
            BattleTabButton.Battle => Battle,
            BattleTabButton.CallToArms => CallToArms,
            BattleTabButton.Alarm => Alarm,
            _ => false,
        };
    }
}

/// <summary>Menu 43 as DK2's HUD runs it (docs/research/battle-tab.md): where its buttons sit,
/// when each shows, and which frame of its art draws. The clicks are the caller's: the blades'
/// right click is <see cref="BattleNavigation"/>, the alarm's <see cref="ThingNavigation.NextAlarm"/>,
/// the cancel's left click a <c>RemoveCallToArmsCommand</c> (game action 0x81) and its right click
/// a glide to <see cref="CallToArmsTile"/>.</summary>
/// <remarks>Not built: the blades' sound <c>0x30F</c>, which the draw requests on every frame with
/// an odd phase although the owner hears one sound at a battle's start (Q775). The fly-in notice
/// is <see cref="BattleFlyIn"/> (ADR-0774).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/battle-tab.md#6-drawing--strongly-inferred")]
public static class BattleTabView
{
    /// <summary>The menu's left edge and its live top with the panel expanded (<c>0x00417170</c>
    /// moves menus 0x2B, 0x12 and 0x27 to the tab top, 1460, or 1772 collapsed), in the
    /// 2560 x 1920 reference screen.</summary>
    public const int MenuX = 2080, MenuY = 1460, ButtonSize = 128;

    /// <summary>The real-clock milliseconds per phase (<c>0x006D3948</c>).</summary>
    public const int PhaseMilliseconds = 150;

    /// <summary>The Alarm shot (trap definition shot id <c>+0xBB</c>).</summary>
    public const byte AlarmShotTypeId = 13;

    /// <summary>Text.str 2165 "Zoom to Battle", the blades' only text; 2166 is its status line.</summary>
    public const int BattleText = 2165, BattleStatusText = 2166;

    private static readonly string[] BattleArt =
        ["GUI\\Icons\\a-fight-00", "GUI\\Icons\\a-fight-01", "GUI\\Icons\\a-fight-02", "GUI\\Icons\\a-fight-03"];
    private static readonly string[] CallToArmsArt = ["GUI\\Spells\\call_to_arms-kill", "GUI\\Spells\\call_to_arms-kill-2"];
    private static readonly string[] AlarmArt =
        ["GUI\\Icons\\a-alarm-00", "GUI\\Icons\\a-alarm-01", "GUI\\Icons\\a-alarm-02", "GUI\\Icons\\a-alarm-03"];

    /// <summary>A button's left edge in the menu (its static widget record).</summary>
    public static int LocalX(BattleTabButton button)
    {
        return button switch
        {
            BattleTabButton.Battle => 0,
            BattleTabButton.CallToArms => 136,
            BattleTabButton.Alarm => 264,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
    }

    /// <summary>The menu's top in the reference screen: <see cref="MenuY"/>, dropped with the panel when it is
    /// collapsed.</summary>
    public static int MenuTop(bool collapsed)
    {
        return MenuY + (collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0);
    }

    /// <summary>The menu's left edge on <paramref name="screen"/>: 480 wide at <see cref="MenuX"/>, it ends at the
    /// reference's right edge, so it stays flush right on a wider or narrower screen (<see cref="HudScreen.FromRight"/>),
    /// over the bottom panel's metal rail end, which sits the same way.</summary>
    public static float MenuLeft(HudScreen screen)
    {
        return screen.FromRight(MenuX);
    }

    /// <summary>A button's square in native virtual units (1920 high) on <paramref name="screen"/>.</summary>
    public static HudRect Rect(BattleTabButton button, bool collapsed, HudScreen screen)
    {
        return new HudRect(MenuLeft(screen) + LocalX(button), MenuTop(collapsed), ButtonSize, ButtonSize);
    }

    /// <summary>A press's action. The blades and the
    /// alarm take only the right click (their records have no left-click callback), the cancel both.</summary>
    /// <param name="mouse">0 left, 1 right.</param>
    public static BattleTabAction Action(BattleTabButton button, int mouse)
    {
        return (button, mouse) switch
        {
            (BattleTabButton.Battle, 1) => BattleTabAction.ZoomToBattle,
            (BattleTabButton.CallToArms, 0) => BattleTabAction.CancelCallToArms,
            (BattleTabButton.CallToArms, 1) => BattleTabAction.GoToCallToArms,
            (BattleTabButton.Alarm, 1) => BattleTabAction.NextAlarm,
            _ => BattleTabAction.None,
        };
    }

    /// <summary>A press on menu 43:
    /// nothing while input is suppressed or a menu is modal; otherwise <see cref="Action"/>'s, the blades' zoom as the F
    /// key's (<see cref="ClassicHudAction.NavigateFight"/>), the cancel's <see cref="RemoveCallToArmsCommand"/> (action 0x81)
    /// or glide to <see cref="CallToArmsTile"/>, and the alarm's glide to <see cref="ThingNavigation.NextAlarm"/> over
    /// <see cref="TrapEntries"/>, walked on <paramref name="traps"/>, the Workshop trap cycle's last trap.</summary>
    /// <param name="mouse">0 left, 1 right.</param>
    /// <param name="scratch">A reused list the trap entries are written into.</param>
    public static BattleTabClick Click(BattleTabButton button, int mouse, ClassicHudControls controls, WorldState world,
        GameDatabase database, PlayerId viewer, ThingNavigation traps, List<ThingNavigationEntry> scratch)
    {
        if (controls is null)
        {
            throw new ArgumentNullException(nameof(controls));
        }

        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (traps is null)
        {
            throw new ArgumentNullException(nameof(traps));
        }

        if (controls.InputSuppressed || controls.Modal)
        {
            return default;
        }

        switch (Action(button, mouse))
        {
            case BattleTabAction.ZoomToBattle:
                return new BattleTabClick(ClassicHudAction.NavigateFight, null, null);
            case BattleTabAction.CancelCallToArms:
                return new BattleTabClick(ClassicHudAction.None, new RemoveCallToArmsCommand(viewer), null);
            case BattleTabAction.GoToCallToArms:
                return new BattleTabClick(ClassicHudAction.None, null, CallToArmsTile(world, viewer));
            case BattleTabAction.NextAlarm:
                TrapEntries(world.Traps, database, world.CurrentTick, scratch);
                return new BattleTabClick(ClassicHudAction.None, null, traps.NextAlarm(scratch));
            default:
                return default;
        }
    }

    /// <summary>Every art name the menu draws, for preloading and the install check.</summary>
    public static IEnumerable<string> AllArt
    {
        get
        {
            return BattleArt.Concat(CallToArmsArt).Concat(AlarmArt);
        }
    }

    /// <summary>The phase for a real-clock time in milliseconds.</summary>
    public static int Phase(long milliseconds)
    {
        return (int)(milliseconds / PhaseMilliseconds);
    }

    /// <summary><c>0x00412AF3..0x00412BA7</c>: the blades and the alarm cycle four frames, art +
    /// (phase &amp; 3); the cancel alternates two, art + ((phase &gt;&gt; 1) &amp; 1).</summary>
    public static string Art(BattleTabButton button, int phase)
    {
        return button switch
        {
            BattleTabButton.Battle => BattleArt[phase & 3],
            BattleTabButton.CallToArms => CallToArmsArt[(phase >> 1) & 1],
            BattleTabButton.Alarm => AlarmArt[phase & 3],
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
    }

    /// <summary>The three frame callbacks for <paramref name="viewer"/>.</summary>
    public static BattleTabSnapshot Capture(WorldState world, GameDatabase database, PlayerId viewer,
        IReadOnlyList<BattleViewSnapshot> battles)
    {
        return new BattleTabSnapshot(BattleShows(battles, viewer), CallToArmsShows(world, viewer), AlarmShows(world, database, viewer));
    }

    /// <summary><c>0x004269D0</c>: any live battle record holding a creature of the viewer.</summary>
    public static bool BattleShows(IReadOnlyList<BattleViewSnapshot> battles, PlayerId viewer)
    {
        for (int i = 0; i < battles.Count; i++)
        {
            if (battles[i].MemberCount > 0 && battles[i].Owners.Contains(viewer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>0x00426920</c> through <c>0x004C00D0</c>: the call is on and its flag is live,
    /// not in a hand (object <c>+0x146</c> bit 0x10) and activated or recalling (process state 13
    /// or 14). The flag's kind is not tested.</summary>
    public static bool CallToArmsShows(WorldState world, PlayerId viewer)
    {
        if (!world.HasPlayer(viewer))
        {
            return false;
        }

        CallToArmsState call = world.Player(viewer).CallToArms;
        return call.On && world.ObjectById(call.ObjectId) is { InHand: false } flag
            && flag.ProcessState is CallToArmsRules.Activated or CallToArmsRules.Recalling;
    }

    /// <summary>The cancel's right click (<c>0x00426830</c>): the call's tile, while the call is on.</summary>
    public static TilePos? CallToArmsTile(WorldState world, PlayerId viewer)
    {
        return world.HasPlayer(viewer) && world.Player(viewer).CallToArms is { On: true } call ? call.Tile : null;
    }

    /// <summary><c>0x00426B90</c>: any trap of the viewer whose shot is the alarm's and whose
    /// recharge deadline the turn has not passed (<c>0x004AA2F0</c>).</summary>
    public static bool AlarmShows(WorldState world, GameDatabase database, PlayerId viewer)
    {
        return AlarmShows(world.Traps, database, viewer, world.CurrentTick);
    }

    public static bool AlarmShows(IReadOnlyList<TrapState> traps, GameDatabase database, PlayerId viewer, GameTick tick)
    {
        for (int i = 0; i < traps.Count; i++)
        {
            TrapState trap = traps[i];
            if (trap.PlayerId == viewer.Value && IsAlarm(database, trap) && Ringing(trap, tick))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The traps in the order DK2's per-player list walks them, newest first
    /// (<c>Thing_LinkToOwner</c> <c>0x004B4C30</c> links at the head; ours is descending id), with
    /// what the alarm's cycle reads, into <paramref name="into"/> (cleared first).</summary>
    public static void TrapEntries(IReadOnlyList<TrapState> traps, GameDatabase database, GameTick tick,
        List<ThingNavigationEntry> into)
    {
        into.Clear();
        for (int i = traps.Count - 1; i >= 0; i--)
        {
            TrapState trap = traps[i];
            bool alarm = IsAlarm(database, trap);
            into.Add(new ThingNavigationEntry(trap.Id, new PlayerId(trap.PlayerId), trap.TrapId,
                new TilePos(trap.X, trap.Y), alarm, alarm && Ringing(trap, tick)));
        }
    }

    public static bool IsAlarm(GameDatabase database, TrapState trap)
    {
        return database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition) && definition.ShotTypeId == AlarmShotTypeId;
    }

    public static bool Ringing(TrapState trap, GameTick tick)
    {
        return MinimapMarkerRules.IsAlarmRingActive(tick, new GameTick(trap.RechargeAt));
    }
}

/// <summary>Menu 43's hover text: over the blades,
/// Text.str 2166 "Battle Tab: right-click to zoom, click again to cycle." as the status line and 2165 "Zoom to
/// Battle" as the name (seen in DK2, 2026-11-13, battle-tab.md section 8); the cancel and the alarm have no text.
/// The blades' text is built once.</summary>
public sealed class BattleTabHoverText
{
    private readonly Func<uint, string> _text;
    private HoverTextSnapshot _battle;

    /// <param name="text">A <c>Text.str</c> string by id.</param>
    public BattleTabHoverText(Func<uint, string> text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public HoverTextSnapshot For(BattleTabButton hovered)
    {
        if (hovered != BattleTabButton.Battle)
        {
            return HoverTextSnapshot.Empty;
        }

        if (_battle.TooltipName == null)
        {
            _battle = new HoverTextSnapshot(_text(BattleTabView.BattleStatusText), _text(BattleTabView.BattleText), default);
        }

        return _battle;
    }
}
