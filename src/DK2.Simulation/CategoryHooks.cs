// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>Which of a category's three hook slots: DK2's tables at <c>0x0069968C</c>,
/// <c>0x0069969C</c> and <c>0x006996AC</c>, each <c>+category * 0x40</c>.</summary>
public enum HookKind : byte
{
    Enter,
    Update,
    Leave,
}

/// <summary>How much of one DK2 hook body is built here.</summary>
public enum HookStatus : byte
{
    /// <summary>All of it.</summary>
    Built,

    /// <summary>Part of it; the slot's note says which part is missing.</summary>
    Partial,

    /// <summary>DK2's success stub <c>0x004B4C20</c> (<c>MOV EAX,1; RET</c>): nothing to build.</summary>
    Stub,

    /// <summary>None of it; the note gives the reason or the question.</summary>
    NotBuilt,
}

/// <summary>One filled slot of DK2's category hook table (<c>state-categories.md</c> §7.1),
/// with what ours does for it.</summary>
public sealed class HookSlot
{
    internal HookSlot(CreatureStateCategory category, HookKind kind, uint address, HookStatus status, string note)
    {
        Category = category;
        Kind = kind;
        Address = address;
        Status = status;
        Note = note;
    }

    public CreatureStateCategory Category { get; }
    public HookKind Kind { get; }

    /// <summary>The DKII.EXE function in the slot.</summary>
    public uint Address { get; }

    public HookStatus Status { get; }

    /// <summary>What is built, what is not and why, with the question that blocks it.</summary>
    public string Note { get; }

    /// <summary>For an update slot that is built, where the per-tick arm lives: the systems run
    /// it off <see cref="CreatureStateMachine.UpdatingCategory"/>, not this table.</summary>
    public Type? UpdateArm { get; internal init; }

    /// <summary>The member of <see cref="UpdateArm"/>.</summary>
    public string? UpdateArmMember { get; internal init; }

    internal EnterHook? Enter { get; init; }
    internal LeaveHook? Leave { get; init; }

    /// <summary>An enter hook that seeds the countdown itself, after a test that may refuse, where
    /// DK2's does; <see cref="CategoryHooks.RunEnter"/> then skips the shared seed.</summary>
    public bool SeedsCountdown { get; internal init; }

    /// <summary>Whether <see cref="CreatureStateMachine"/> runs code of ours from this slot.</summary>
    public bool IsDispatched => Enter is not null || Leave is not null;
}

/// <summary>An enter hook. False refuses the move: <c>SetState</c> then leaves the creature in
/// state 1 with the old category's leave hook already run.</summary>
internal delegate bool EnterHook(in StateHookContext hooks, CreatureState creature);

/// <summary>A leave hook. DK2 discards its return.</summary>
internal delegate void LeaveHook(in StateHookContext hooks, CreatureState creature);

/// <summary>DK2's category hooks, indexed by <see cref="CreatureStateCategory"/>, and the enter
/// and leave dispatch <see cref="CreatureStateMachine"/> runs in <c>SetState</c>'s order
/// (<c>state-category-hooks.md</c> §5, ADR-0308). Every filled slot of DK2's table has a row in
/// <see cref="Slots"/>, built or not.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/state-category-hooks.md")]
public static class CategoryHooks
{
    private const string Seeded = "the countdown seed is built (the shared step in RunEnter)";
    private const string Claimed = "the claim is built (RoomCapacityRules.Claim, ADR-0309)";
    private const string Release = "releases the room position (ADR-0309); not built: the Crypt occupant "
        + "handover 0x004D8E60 when the place is a Crypt's (state-category-hooks.md §1)";

    private static readonly HookSlot[] All =
    [
        Slot(CreatureStateCategory.Refused, HookKind.Leave, 0x0047A780, HookStatus.NotBuilt,
            "clears +0x3CD, two vectors and +0x3E6 bit 20; the combined role is UNKNOWN (state-category-hooks.md §2)"),
        new(CreatureStateCategory.WorkerJob, HookKind.Leave, 0x004647D0, HookStatus.Partial,
            "the pickup subset is built (ADR-0263); the task removal and completion are not (state-category-hooks.md §2)")
        {
            Leave = static (in StateHookContext _, CreatureState creature) => WorkerPickupRules.OnTaskReleased(creature),
        },

        Claiming(CreatureStateCategory.Research, 0x0047AFB0, HookStatus.Built, "claims the room position (ADR-0309)"),
        Arm(CreatureStateCategory.Research, 0x0047B000, HookStatus.Built, "credit per turn, bonuses per second (ADR-0186)",
            typeof(CreatureJobSystem), "Research"),
        Releasing(CreatureStateCategory.Research, 0x0047B920, Release),

        new(CreatureStateCategory.Manufacture, HookKind.Enter, 0x0047B0A0, HookStatus.Partial,
            "player +0x8A up on the tile's keeper, then " + Claimed + " (ADR-0322); not modelled: the clear "
            + "of state +0xB9, which nothing reads (leave-hook-bodies.md)")
        {
            Enter = static (in StateHookContext hooks, CreatureState creature) => ManufactureRules.Enter(in hooks, creature),
        },
        Arm(CreatureStateCategory.Manufacture, 0x0047B190, HookStatus.Partial,
            "credit per turn (ADR-0203); not built: a zero credit failing the hook, and the turn % v19 call "
            + "to 0x004F2300 (state-category-hooks.md §3.4)",
            typeof(CreatureJobSystem), "Manufacture"),
        new(CreatureStateCategory.Manufacture, HookKind.Leave, 0x0047B120, HookStatus.Partial,
            "player +0x8A down on the tile's keeper, with no floor (ADR-0322), then " + Release)
        {
            Leave = static (in StateHookContext hooks, CreatureState creature) => ManufactureRules.Leave(in hooks, creature),
        },

        Slot(CreatureStateCategory.Lair, HookKind.Enter, 0x004B4C20, HookStatus.Stub, "success stub"),
        Arm(CreatureStateCategory.Lair, 0x0047AC40, HookStatus.Partial,
            "anger cools (ADR-0185); the healing predicate in 0x004DFF00 is unread (Q123)",
            typeof(LairRules), nameof(LairRules.CoolsAngerInLair)),

        Slot(CreatureStateCategory.Eating, HookKind.Update, 0x004B4C20, HookStatus.Stub, "success stub"),

        Slot(CreatureStateCategory.Sleeping, HookKind.Enter, 0x004B4C20, HookStatus.Stub, "success stub"),
        Arm(CreatureStateCategory.Sleeping, 0x0047AC00, HookStatus.Partial,
            "anger cools (ADR-0185); state 83's 0x00477B20(1) is not built (state-category-hooks.md §3)",
            typeof(LairRules), nameof(LairRules.CoolsAngerInLair)),
        Slot(CreatureStateCategory.Sleeping, HookKind.Leave, 0x0047AB90, HookStatus.NotBuilt,
            "presentation only: sets the draw bit (thing +0xF3 bit 0) on the lair and the creature, which "
            + "ours does not keep (leave-hook-bodies.md)"),

        Slot(CreatureStateCategory.Sulking, HookKind.Enter, 0x004B4C20, HookStatus.Stub, "success stub"),
        Arm(CreatureStateCategory.Sulking, 0x0047AC70, HookStatus.Partial,
            "anger cools (ADR-0185); its return, 0x004871A0(5), is not built (Q438)",
            typeof(LairRules), nameof(LairRules.CoolsAngerInLair)),

        Claiming(CreatureStateCategory.GuardPost, 0x0047AFB0, HookStatus.Built, "claims the room position (ADR-0309)"),
        Releasing(CreatureStateCategory.GuardPost, 0x0047B920, Release),

        Claiming(CreatureStateCategory.Temple, 0x0047B8C0, HookStatus.Built, Seeded + "; " + Claimed),
        Arm(CreatureStateCategory.Temple, 0x0047B970, HookStatus.Built, "prayer every fifth turn (ADR-0186, ADR-0192)",
            typeof(CreatureJobSystem), "Pray"),
        Releasing(CreatureStateCategory.Temple, 0x0047B920, Release),

        new(CreatureStateCategory.Training, HookKind.Enter, 0x0047B230, HookStatus.Partial,
            "the gold test with message 55, then the seed, then the claim (ADR-0313); not built: clearing "
            + "+0xB4, the count behind the floating -20, which ours does not keep")
        {
            SeedsCountdown = true,
            Enter = static (in StateHookContext hooks, CreatureState creature) =>
            {
                WorldState world = hooks.World;
                if (!TrainingRules.CanAfford(world, hooks.Database, creature.OwnerPlayerId))
                {
                    world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.TrainingUnpaid,
                        TrainingRules.UnpaidMessageVariant, 0);
                    return false;
                }

                // Seeded before the claim, so a full room still leaves it written, as DK2's does.
                creature.HookCountdown = CreatureStateMachine.HookCountdownSeed;
                return RoomCapacityRules.Claim(in hooks, creature);
            },
        },
        Arm(CreatureStateCategory.Training, 0x0047B2D0, HookStatus.Partial,
            "experience per turn, the charge every fifth (Q205); not built: at the level cap DK2 returns 1 with "
            + "no charge and no countdown step, where ours still charges, and message 55 when the keeper cannot pay",
            typeof(CreatureJobSystem), "Train"),
        Releasing(CreatureStateCategory.Training, 0x0047B920, Release),

        new(CreatureStateCategory.Prison, HookKind.Enter, 0x0047B740, HookStatus.Partial,
            Seeded + "; " + Claimed + ", then creature trigger event 7 once it takes (ADR-0311); not built: "
            + "the claim's position tests and overcrowding roll (Q504)")
        {
            Enter = static (in StateHookContext hooks, CreatureState creature) =>
            {
                // DK2 returns 0 on a refused claim before event 7. Our claim lacks the Prison's position
                // tests and capacity (Q504), so 7 also fires on a rim tile or into a full Prison, where
                // DK2's claim would refuse.
                if (!RoomCapacityRules.Claim(in hooks, creature)) return false;
                hooks.FireCreatureEvent(creature, CreatureTriggerEvent.Imprisoned);
                return true;
            },
        },
        Arm(CreatureStateCategory.Prison, 0x0047B7C0, HookStatus.Built, "the drain every seventeenth turn (ADR-0191)",
            typeof(PrisonSystem), "Wither"),
        Releasing(CreatureStateCategory.Prison, 0x0047B920, Release),

        Claiming(CreatureStateCategory.Torture, 0x0047B430, HookStatus.Built, Claimed + "; " + Seeded),
        Arm(CreatureStateCategory.Torture, 0x0047B590, HookStatus.Partial,
            "duration, hurt, mood and conversion (ADR-0193); the information is ADR-0230's",
            typeof(TortureSystem), nameof(TortureSystem.Execute)),
        new(CreatureStateCategory.Torture, HookKind.Leave, 0x0047B4A0, HookStatus.Partial,
            "DroppedToDoJob cleared unless TortureVoluntary, the flag cleared (ADR-0322), then " + Release
            + "; not modelled: the device's draw bit and the chair's spark bit (leave-hook-bodies.md)")
        {
            Leave = static (in StateHookContext hooks, CreatureState creature) => TortureRules.Leave(in hooks, creature),
        },

        new(CreatureStateCategory.CombatPit, HookKind.Enter, 0x0047AAE0, HookStatus.Partial,
            Seeded + "; the CombatPitFighter flag on the pit's floor (DK2 tests the floor height 0x400, ours an "
            + "interior tile), then the claim, which gives a fighter a "
            + "place of 0 units (ADR-0309); CombatPitSystem.Move also sets the flag, judged by the current "
            + "category rather than the saved caller's")
        {
            Enter = static (in StateHookContext hooks, CreatureState creature) =>
            {
                WorldState world = hooks.World;
                if (world.Contains(creature.Tile))
                {
                    int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
                    if (CombatPitRules.IsFloor(CombatPitRules.PitAt(world, hooks.Database, here), here))
                        creature.CombatPitFighter = true;
                }

                return RoomCapacityRules.Claim(in hooks, creature);
            },
        },
        Slot(CreatureStateCategory.CombatPit, HookKind.Update, 0x004B4C20, HookStatus.Stub, "success stub"),
        Releasing(CreatureStateCategory.CombatPit, 0x0047B920, Release),

        new(CreatureStateCategory.Casino, HookKind.Enter, 0x0047ACB0, HookStatus.Partial,
            "the lever test on the keeper's own Casino, the purse test, then the seed, then the claim "
            + "(ADR-0313); not built: the jackpot party (player +0xF4A) lifting the purse test")
        {
            SeedsCountdown = true,
            Enter = static (in StateHookContext hooks, CreatureState creature) =>
            {
                if (CasinoRules.EnterRefusal(hooks.World, hooks.Database, creature) is not null) return false;
                creature.HookCountdown = CreatureStateMachine.HookCountdownSeed;
                return RoomCapacityRules.Claim(in hooks, creature);
            },
        },
        Arm(CreatureStateCategory.Casino, 0x0047AD70, HookStatus.Partial,
            "the fearless countdown (ADR-0322), calm, anger and the purse (ADR-0187, ADR-0188); the jackpot "
            + "is not built",
            typeof(CreatureJobSystem), "Gamble"),
        new(CreatureStateCategory.Casino, HookKind.Leave, 0x0047AF20, HookStatus.Partial,
            "a fearless gambler's flag turned into FearlessUntil, now + v19 x v203 (ADR-0322), then " + Release)
        {
            Leave = static (in StateHookContext hooks, CreatureState creature) => CasinoRules.Leave(in hooks, creature),
        },

        new(CreatureStateCategory.Combat, HookKind.Enter, 0x0047A810, HookStatus.Partial,
            "the stop and the running action ended (ADR-0319); not modelled: ReportingTo +0x3D5 and the "
            + "six words +0x64-+0x68, +0x95-+0x99 (combat-category-hooks.md §3)")
        {
            Enter = static (in StateHookContext _, CreatureState creature) => CombatRules.EnterCombat(creature),
        },
        Slot(CreatureStateCategory.Combat, HookKind.Update, 0x0047A870, HookStatus.NotBuilt,
            "combat experience in 199/200 (CreatureLevelRules: fighting earns none yet)"),
        new(CreatureStateCategory.Combat, HookKind.Leave, 0x0047A980, HookStatus.Partial,
            "0x0045ADC0 (forced target, engagement, every attacker) and the walk speed, on every route out "
            + "(ADR-0319); not modelled: ReportingTo +0x3D5, +0x232 bit 7 and +0x233 bit 0 (combat-category-hooks.md §3)")
        {
            Leave = static (in StateHookContext hooks, CreatureState creature) => CombatRules.LeaveCombat(in hooks, creature),
        },

        Slot(CreatureStateCategory.Dying, HookKind.Update, 0x004B4C20, HookStatus.Stub, "success stub"),

        Slot(CreatureStateCategory.TrapDisarm, HookKind.Leave, 0x00459130, HookStatus.NotBuilt,
            "clears a trap's +0x115 and creature +0x43D; no disarm state is built (Q455)"),

        new(CreatureStateCategory.CallToArms, HookKind.Enter, 0x00455E70, HookStatus.Built,
            "the run speed on, 0x004887D0(1, 0)")
        {
            Enter = static (in StateHookContext _, CreatureState creature) =>
            {
                creature.Running = true;
                return true;
            },
        },
        new(CreatureStateCategory.CallToArms, HookKind.Leave, 0x00455E90, HookStatus.Built,
            "the run speed off, 0x004887D0(0, 0)")
        {
            Leave = static (in StateHookContext _, CreatureState creature) => creature.Running = false,
        },

        new(CreatureStateCategory.ReportingDanger, HookKind.Leave, 0x004752A0, HookStatus.Built,
            "clears RebelActive; why leaving this category ends a rebellion is UNKNOWN (Q411)")
        {
            Leave = static (in StateHookContext _, CreatureState creature) => creature.RebelActive = false,
        },

        Slot(CreatureStateCategory.Unnamed28, HookKind.Update, 0x0047A9B0, HookStatus.NotBuilt,
            "-10 health a tick unless the owner has won; no category-28 state is built"),

        Claiming(CreatureStateCategory.Spectating, 0x0047A9E0, HookStatus.Partial,
            Seeded + "; " + Claimed + "; the CombatPitFighter clear is at the call site (CombatPitSpectatorRules)"),
        Arm(CreatureStateCategory.Spectating, 0x0047AA70, HookStatus.Built, "variable 135 to three buckets every fifth turn (ADR-0186)",
            typeof(CreatureJobSystem), "Spectate"),
        Releasing(CreatureStateCategory.Spectating, 0x0047B920, Release),
    ];

    private static readonly EnterHook?[] Enters = Index(s => s.Enter);
    private static readonly LeaveHook?[] Leaves = Index(s => s.Leave);
    private static readonly bool[] OwnSeed = OwnSeeds();

    /// <summary>Every filled slot of DK2's table, by category, then enter, update, leave.</summary>
    public static IReadOnlyList<HookSlot> Slots => All;

    /// <summary>The target category's enter hook, after the seed every countdown enter hook writes
    /// (variable 19, <see cref="CreatureStateMachine.HookCountdownSeed"/>). Ours has one countdown
    /// field for the six bytes DK2 keeps, so it is seeded on entering any category; only a
    /// countdown category's update reads it (ADR-0192). Training and the Casino seed it in their
    /// hooks, after the tests that refuse (ADR-0313).</summary>
    internal static bool RunEnter(in StateHookContext hooks, CreatureStateCategory category, CreatureState creature)
    {
        if (!OwnSeed[(int)category]) creature.HookCountdown = CreatureStateMachine.HookCountdownSeed;
        EnterHook? enter = Enters[(int)category];
        return enter is null || enter(in hooks, creature);
    }

    /// <summary>Why the category's enter hook refused before its claim, or null: the log's reason
    /// for a refusal that is not the room's (ADR-0313). The tests are pure, so asking again after
    /// the hook refused gives its answer.</summary>
    internal static string? RefusedBeforeClaim(
        in StateHookContext hooks, CreatureStateCategory category, CreatureState creature) => category switch
    {
        CreatureStateCategory.Training =>
            TrainingRules.CanAfford(hooks.World, hooks.Database, creature.OwnerPlayerId) ? null : "keeper_cannot_pay",
        CreatureStateCategory.Casino => CasinoRules.EnterRefusal(hooks.World, hooks.Database, creature),
        _ => null,
    };

    /// <summary>The old category's leave hook, if it has one.</summary>
    internal static void RunLeave(in StateHookContext hooks, CreatureStateCategory category, CreatureState creature) =>
        Leaves[(int)category]?.Invoke(in hooks, creature);

    private static HookSlot Slot(
        CreatureStateCategory category, HookKind kind, uint address, HookStatus status, string note) =>
        new(category, kind, address, status, note);

    /// <summary>An enter slot whose only dispatched step is the room claim.</summary>
    private static HookSlot Claiming(
        CreatureStateCategory category, uint address, HookStatus status, string note) =>
        new(category, HookKind.Enter, address, status, note)
        {
            Enter = static (in StateHookContext hooks, CreatureState creature) => RoomCapacityRules.Claim(in hooks, creature),
        };

    /// <summary>A leave slot whose only dispatched step is the room release.</summary>
    private static HookSlot Releasing(CreatureStateCategory category, uint address, string note) =>
        new(category, HookKind.Leave, address, HookStatus.Partial, note)
        {
            Leave = static (in StateHookContext _, CreatureState creature) => RoomCapacityRules.Release(creature),
        };

    private static HookSlot Arm(
        CreatureStateCategory category, uint address, HookStatus status, string note, Type arm, string member) =>
        new(category, HookKind.Update, address, status, note) { UpdateArm = arm, UpdateArmMember = member };

    private static bool[] OwnSeeds()
    {
        var byCategory = new bool[CreatureStateTable.CategoryCount];
        foreach (HookSlot slot in All)
        {
            if (slot.SeedsCountdown) byCategory[(int)slot.Category] = true;
        }

        return byCategory;
    }

    private static T?[] Index<T>(Func<HookSlot, T?> pick) where T : class
    {
        var byCategory = new T?[CreatureStateTable.CategoryCount];
        foreach (HookSlot slot in All)
        {
            if (pick(slot) is T hook) byCategory[(int)slot.Category] = hook;
        }

        return byCategory;
    }
}
