// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

using Cat = CreatureStateCategory;

/// <summary><see cref="CategoryHooks"/> against DK2's own table (<c>state-categories.md</c> §7.1,
/// bodies in <c>state-category-hooks.md</c> §2–§3), and the dispatch <c>SetState</c> runs
/// (ADR-0308).</summary>
public class CategoryHooksTests
{
    // Every filled slot of DK2's table: category, enter, update, leave (0 = empty).
    private static readonly (Cat Category, uint Enter, uint Update, uint Leave)[] Dk2Table =
    [
        (Cat.Refused, 0, 0, 0x0047A780),
        (Cat.WorkerJob, 0, 0, 0x004647D0),
        (Cat.Research, 0x0047AFB0, 0x0047B000, 0x0047B920),
        (Cat.Manufacture, 0x0047B0A0, 0x0047B190, 0x0047B120),
        (Cat.Lair, 0x004B4C20, 0x0047AC40, 0),
        (Cat.Eating, 0, 0x004B4C20, 0),
        (Cat.Sleeping, 0x004B4C20, 0x0047AC00, 0x0047AB90),
        (Cat.Sulking, 0x004B4C20, 0x0047AC70, 0),
        (Cat.GuardPost, 0x0047AFB0, 0, 0x0047B920),
        (Cat.Temple, 0x0047B8C0, 0x0047B970, 0x0047B920),
        (Cat.Training, 0x0047B230, 0x0047B2D0, 0x0047B920),
        (Cat.Prison, 0x0047B740, 0x0047B7C0, 0x0047B920),
        (Cat.Torture, 0x0047B430, 0x0047B590, 0x0047B4A0),
        (Cat.CombatPit, 0x0047AAE0, 0x004B4C20, 0x0047B920),
        (Cat.Casino, 0x0047ACB0, 0x0047AD70, 0x0047AF20),
        (Cat.Combat, 0x0047A810, 0x0047A870, 0x0047A980),
        (Cat.Dying, 0, 0x004B4C20, 0),
        (Cat.TrapDisarm, 0, 0, 0x00459130),
        (Cat.CallToArms, 0x00455E70, 0, 0x00455E90),
        (Cat.ReportingDanger, 0, 0, 0x004752A0),
        (Cat.Unnamed28, 0, 0x0047A9B0, 0),
        (Cat.Spectating, 0x0047A9E0, 0x0047AA70, 0x0047B920),
    ];

    private const uint SuccessStub = 0x004B4C20;

    [Fact]
    public void EveryFilledSlotOfDk2sTableHasExactlyOneRow_AndNoRowNamesASlotDk2LeavesEmpty()
    {
        var expected = new HashSet<(Cat, HookKind, uint)>();
        foreach ((Cat category, uint enter, uint update, uint leave) in Dk2Table)
        {
            if (enter != 0) expected.Add((category, HookKind.Enter, enter));
            if (update != 0) expected.Add((category, HookKind.Update, update));
            if (leave != 0) expected.Add((category, HookKind.Leave, leave));
        }

        var actual = CategoryHooks.Slots.Select(s => (s.Category, s.Kind, s.Address)).ToList();

        Assert.Equal(48, expected.Count);
        Assert.Equal(actual.Count, actual.Distinct().Count());
        Assert.Empty(expected.Except(actual));
        Assert.Empty(actual.Except(expected));
    }

    [Fact]
    public void EverySlotIsBuiltPartlyBuiltAStubOrNotBuiltWithAReason()
    {
        string[] wrong = CategoryHooks.Slots.Where(Wrong).Select(s => $"{s.Category} {s.Kind} 0x{s.Address:X8}").ToArray();
        Assert.True(wrong.Length == 0, string.Join("\n", wrong));

        static bool Wrong(HookSlot s) => string.IsNullOrWhiteSpace(s.Note) || s.Status switch
        {
            HookStatus.Stub => s.Address != SuccessStub || s.IsDispatched,
            HookStatus.NotBuilt => s.IsDispatched || s.UpdateArm is not null,
            // Built or partly built: code of ours runs for it, from SetState or from a system's
            // update arm, or (an enter) the shared countdown seed is the built part.
            _ => s.Kind == HookKind.Update ? s.UpdateArm is null
                : !s.IsDispatched && !s.Note.Contains("countdown seed"),
        };
    }

    [Fact]
    public void EveryUpdateArmNamesAMemberThatExists()
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        string[] missing = CategoryHooks.Slots
            .Where(s => s.UpdateArm is not null && s.UpdateArm.GetMember(s.UpdateArmMember!, any).Length == 0)
            .Select(s => $"{s.Category}: {s.UpdateArm!.Name}.{s.UpdateArmMember}")
            .ToArray();

        Assert.True(missing.Length == 0, string.Join("\n", missing));
    }

    [Fact]
    public void SetStateDispatchesWorkerJobsLeaveCallToArmsAndEveryRoomClaimAndRelease()
    {
        var dispatched = CategoryHooks.Slots.Where(s => s.IsDispatched).Select(s => (s.Category, s.Kind)).ToHashSet();
        var expected = new HashSet<(Cat, HookKind)>
        {
            (Cat.WorkerJob, HookKind.Leave), (Cat.CallToArms, HookKind.Enter), (Cat.CallToArms, HookKind.Leave),
        };
        // The nine claiming enter hooks of state-category-hooks.md §1, ten slots with GuardPost.
        Cat[] claimers =
        [
            Cat.Research, Cat.Manufacture, Cat.GuardPost, Cat.Temple, Cat.Training,
            Cat.Prison, Cat.Torture, Cat.CombatPit, Cat.Casino, Cat.Spectating,
        ];
        foreach (Cat claiming in claimers)
        {
            expected.Add((claiming, HookKind.Enter));
            expected.Add((claiming, HookKind.Leave));
        }

        Assert.Equal(expected, dispatched);
    }

    // --- Call to Arms enter (0x00455E70) and leave (0x00455E90) ---------------------------

    [Fact]
    public void CallToArmsEnter_SetsTheRunSpeed_OnEnteringTheCategory()
    {
        CreatureState creature = Idle();

        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CallToArmsRules.GoingState));

        Assert.True(creature.Running);
    }

    [Fact]
    public void CallToArmsEnter_DoesNotRunOnAWalkBetweenTwoCallToArmsStates()
    {
        CreatureState creature = Idle();
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CallToArmsRules.GoingState));
        creature.Running = false;

        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, CallToArmsRules.ArrivedState);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));

        Assert.Equal(CallToArmsRules.ArrivedState, creature.Dk2StateId);
        Assert.False(creature.Running);
    }

    [Fact]
    public void CallToArmsLeave_TakesTheRunSpeedOff_OnLeavingTheCategory()
    {
        CreatureState creature = Idle();
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CallToArmsRules.GoingState));

        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CreatureStateMachine.Idle));

        Assert.False(creature.Running);
    }

    [Fact]
    public void CallToArmsLeave_DoesNotRunOnAWalkBetweenTwoCallToArmsStates()
    {
        CreatureState creature = Idle();
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CallToArmsRules.GoingState));

        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, CallToArmsRules.ArrivedState);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));

        Assert.True(creature.Running);
    }

    // --- WorkerJob leave (0x004647D0), its pickup subset (ADR-0263) -----------------------

    [Fact]
    public void WorkerJobLeave_RestoresTheWorkerBits_OnLeavingTheCategory()
    {
        var creature = new CreatureState { Id = 1, Dk2StateId = ImpStates.AtThePrisonDoor, Flags = 0xA6 };

        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CreatureStateMachine.Idle));

        Assert.Equal(0xAF, creature.Flags);
    }

    [Fact]
    public void WorkerJobLeave_DoesNotRunOnAWalkBetweenTwoWorkerStates()
    {
        var creature = new CreatureState { Id = 1, Dk2StateId = ImpStates.AtThePrisonDoor, Flags = 0xA6 };

        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, ImpStates.LeavingThePrisoner);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));

        Assert.Equal(ImpStates.LeavingThePrisoner, creature.Dk2StateId);
        Assert.Equal(0xA6, creature.Flags);
    }

    // --- The countdown seed every countdown enter hook writes (ADR-0192) ------------------

    [Fact]
    public void TheCountdownSeed_IsWrittenOnEnteringACategory()
    {
        CreatureState creature = Idle();
        creature.HookCountdown = 0;

        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CallToArmsRules.GoingState));

        Assert.Equal(CreatureStateMachine.HookCountdownSeed, creature.HookCountdown);
    }

    [Fact]
    public void TheCountdownSeed_IsNotWrittenOnAWalkBetweenTwoStatesOfOneCategory()
    {
        CreatureState creature = Idle();
        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CallToArmsRules.GoingState));
        creature.HookCountdown = 1;

        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, CallToArmsRules.ArrivedState);
        Assert.True(CreatureStateMachine.Arrive(DetachedHooks.Context, creature));

        Assert.Equal(1, creature.HookCountdown);
    }

    // --- SetState's own order (state-category-hooks.md §5) --------------------------------

    [Fact]
    public void TheInnerSetStateOneOfATravelToTravelMoveIsNeverRefused_WhateverTheResume()
    {
        // DK2 asks the rule for the recursion's move to 1; ours does not, which is safe only
        // because no travel state with any resume is refused it.
        foreach (int travel in new[] { 6, 7, 8, 9 })
        for (int resume = 0; resume < CreatureStateTable.Count; resume++)
        {
            Assert.Equal(StateTransition.Allowed,
                CreatureStateTransitions.Classify(travel, CreatureStateMachine.Idle, resume));
        }
    }

    [Fact]
    public void ALeaveHookSeesTheOldState_SoTheWorkerJobHookReadsTheResumeOfTheWalkItLeaves()
    {
        // 0x004647D0 runs before SetState writes state 1: leaving a walk to 44 for idle, it
        // reads the resume 44, which is on its list (creature-pickup.md).
        var creature = new CreatureState { Id = 1, Dk2StateId = ImpStates.TaskSelection, Flags = 0xA6 };
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, creature, 6, ImpStates.AtThePrisonDoor);

        Assert.True(CreatureStateMachine.TrySetState(DetachedHooks.Context, creature, CreatureStateMachine.Idle));

        Assert.Equal(0xAF, creature.Flags);
    }

    [Fact]
    public void AStateChangeThroughTheHooksAllocatesNothing()
    {
        CreatureState creature = Idle();
        StateHookContext hooks = DetachedHooks.Context;
        Cycle(hooks, creature);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Cycle(hooks, creature);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);

        static void Cycle(in StateHookContext hooks, CreatureState creature)
        {
            CreatureStateMachine.TrySetState(hooks, creature, CallToArmsRules.GoingState);
            CreatureStateMachine.BeginTravel(hooks, creature, 6, CallToArmsRules.ArrivedState);
            CreatureStateMachine.Arrive(hooks, creature);
            CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle);
        }
    }

    private static CreatureState Idle() => new() { Id = 1 };
}
