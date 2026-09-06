// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// The determinism gate. This is the project's load-bearing test: every claim
/// of behavioural fidelity made from M3 onwards is only meaningful if a run is
/// reproducible in the first place.
///
/// It must stay green on every platform and must never require a DK2 install.
/// </summary>
public class DeterminismTests
{
    private const int Ticks = 1000;

    [Fact]
    public void SameSeedAndInput_ProduceIdenticalHashAtEveryTick()
    {
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();

        Assert.Equal(a.StateHash(), b.StateHash());

        for (int t = 0; t < Ticks; t++)
        {
            InputFrame input = InputAt(t);
            a.Tick(input);
            b.Tick(InputAt(t)); // freshly built each time — value equality, not identity

            Assert.Equal(a.CurrentTick, b.CurrentTick);
            Assert.True(a.StateHash() == b.StateHash(),
                $"runs diverged at {a.CurrentTick}");
        }
    }

    [Fact]
    public void DifferentSeed_Diverges()
    {
        // A gate that cannot fail proves nothing. This asserts the hash really
        // does discriminate between runs.
        GameSimulation a = TestWorld.Simulation(seed: 1);
        GameSimulation b = TestWorld.Simulation(seed: 2);

        a.Run(50);
        b.Run(50);

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DifferentInput_Diverges()
    {
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();

        a.Tick(InputFrame.Of(new AddManaCommand(1, 100)));
        b.Tick(InputFrame.Empty);

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void RngPosition_IsPartOfTheHash()
    {
        // Two worlds identical in every visible way, but one has consumed a
        // draw. They must not compare equal: they are about to diverge.
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();

        b.Rng.NextUInt64();

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void EventRecording_DoesNotAffectState()
    {
        GameSimulation quiet = TestWorld.Simulation();
        GameSimulation observed = TestWorld.Simulation();
        var sink = new ListEventSink();
        observed.ObserveWith(sink);

        quiet.Run(100);
        observed.Run(100);

        Assert.Equal(quiet.StateHash(), observed.StateHash());
        Assert.NotEmpty(sink.Events);
    }

    [Fact]
    public void StateHash_IsStableAcrossBuildsAndMachines()
    {
        // A pinned value. Its purpose is to catch the silent killers of
        // cross-machine reproducibility — string GetHashCode, dictionary
        // iteration order, struct layout drift — which every other test here
        // would happily pass.
        //
        // If this fails after a deliberate change to simulation state or to the
        // hash itself, bump GameSimulation.HashVersion and update the value in
        // the same commit. If it fails for any other reason, determinism is
        // broken and the change must not ship.
        GameSimulation sim = TestWorld.Simulation(seed: 0xDEADBEEF);
        sim.Run(500);

        // Bumped with HashVersion 17 -> 18 (Q31, Q65): an object gained the
        // union's Gold member, ObjectState.GoldHeld, because gold on the floor
        // is an object -- type 1, "Gold", carrying its amount in the same four
        // bytes a bed reads a creature id out of. Nothing in this run drops any:
        // the fixture's imps mine and deliver and none of them dies, and no
        // treasury is sold. So what moved is the composition alone, one more
        // field hashed per object in a world that has no objects in it.
        //
        // That is the useful shape for a pin: a field added to a list that is
        // empty here still changes the hash, which is what proves the hash
        // covers the field rather than the value. Snapshot FormatVersion went
        // 17 -> 18 in the same commit.
        //
        // Bumped with HashVersion 16 -> 17 (Q130): the world gained an OBJECT
        // list, and a creature LOST a field. A bed is now a thing standing on a
        // tile -- an ObjectState whose CreatureId says whose it is -- where it
        // used to be CreatureState.LairTileIndex saying which tile was mine.
        // DK2 keeps the relation on the object and the binary settled it
        // (docs/research/objects.md section 7), so there is one copy of the fact
        // instead of two.
        //
        // The run itself moves nothing: the fixture's creatures are working imps
        // and an imp has no bed. What changed is the composition -- one field
        // out of every creature, and a count and an id counter in. Snapshot
        // FormatVersion went 16 -> 17 in the same commit.
        //
        // Bumped with HashVersion 15 -> 16 (Q124): CreatureState gained
        // ExperiencePoints, and unlike the Level field one commit earlier this
        // one actually MOVES during the run. The owner settled what raises a
        // level -- "imps get experience by working... they get exp and passively
        // their level raises" -- so the fixture's working imps now bank 20 a
        // second. 500 ticks is 25 seconds, which is 500 experience against the
        // 3000 a level costs, so nothing is promoted here and what changed is
        // the banked total. Snapshot FormatVersion went 15 -> 16 in the same
        // commit.
        //
        // Version 15 was the Level field itself (Q119), whose arithmetic is a
        // proven no-op at level 1: only the hash composition moved.
        //
        // Bumped with HashVersion 18 -> 19 (Q56): the free-imp supply stopped
        // keeping a countdown and started keeping a DEADLINE, which is what the
        // original does. PlayerState.FreeImpCountdownTicks became NextFreeImpTick
        // and the fixture's keeper -- who has a dungeon heart and starts with no
        // imps -- is now staffed on tick zero instead of five seconds in, so the
        // whole 500-tick run has one more imp in it for longer. Snapshot
        // FormatVersion went 18 -> 19 in the same commit.
        //
        // Bumped with HashVersion 19 -> 20 (Q154): every tile gained a
        // FreshlyDug bit, set when excavation destroys it and cleared by the
        // next change of terrain, so that an imp claims the tile it has just
        // dug before it looks for the next mark -- first of everything in the
        // original's own order. The fixture digs, so the bit is set on real
        // tiles here and the composition moved with it. Snapshot FormatVersion
        // went 19 -> 20 in the same commit. The mover's two-phase diagonal walk
        // (ADR-0059) landed in the same commit and did NOT move this value: the
        // fixture has no off-centre diagonal leg in it.
        //
        // Bumped with HashVersion 20 -> 21 (Q122, ADR-0061): the angry chooser
        // stopped keeping its draw and started keeping a timestamp --
        // CreatureState.AngryChoice became LastAngryChoiceTick, because what
        // the original keeps between selections is when the last one succeeded
        // and nothing else. The fixture's creatures are working imps with an
        // empty angry table, so nothing here ever rolls and the two fields are
        // both zero for all 500 ticks: this value moved on the version alone.
        // Snapshot FormatVersion went 20 -> 21 in the same commit.
        //
        // Bumped with HashVersion 21 -> 22 (ADR-0062): the Hand of Evil.
        // CreatureState gained InHand, PickupTick, LastSlapTick,
        // WorkFasterTicks and JustDropped, and PlayerState gained the hand
        // itself. Nothing in this fixture is ever picked up or slapped, so all
        // six are at their defaults for all 500 ticks and this value moved on
        // the version alone.
        //
        // What did NOT move it is the more interesting half. Efficiency became
        // a real multiplier on every work application in the same commit, and
        // the whole 501-test suite passed unchanged: EfficiencyRules.Scale
        // returns its input at the default 80%, which is the rate every worker
        // here is at, so the arithmetic this project measured in August is
        // untouched by learning what the fifth was. Snapshot FormatVersion went
        // 21 -> 22 in the same commit.
        //
        // Bumped with HashVersion 22 -> 23 (ADR-0063): carrying. CreatureState
        // gained CarriedCreatureId, CarriedByCreatureId and TargetBodyId, all
        // -1 for every creature in this fixture for all 500 ticks, so this
        // moved on the version and the three defaults alone. Snapshot
        // FormatVersion went 22 -> 23 in the same commit.
        //
        // Death changed shape in the same commit and did NOT move this: a
        // creature that leaves a body now becomes a corpse instead of being
        // removed, and the fixture kills nobody.
        //
        // Bumped with HashVersion 26 -> 27 (ADR-0067): eating. The hunger
        // clock stopped saturating and started accumulating - ChickensNeeded
        // became HungerNeed and the periodic tick ADDS HungerFill rather than
        // setting it - and CreatureState gained FoodAttemptTicks. The fixture's
        // creatures are imps, which never get hungry at all, so both fields are
        // zero for all 500 ticks and this value moved on the version and the
        // new field's default alone. Snapshot FormatVersion went 26 -> 27 in
        // the same commit.
        //
        // What did NOT move it is the point: CreatureEatSystem was registered
        // ABOVE CreatureSleepSystem in the same commit, which changes the order
        // every creature is offered its needs in, and the 500 ticks are
        // identical. Imps neither eat nor sleep, so the fixture cannot see it -
        // and saying so is worth more than the green tick, because the next
        // fixture with a goblin in it will.
        //
        // Previous values: 0x154E9041172A7F7F (26),
        // 0xDFF77147B12A64C3 (22), 0x6AF149EE8E20D98A (21),
        // 0x5E96E634C7E5037D (20),
        // 0x12E5615E803C5796 (19), 0x55BEDDA879B4A281 (18),
        // 0x6B5ECAD0E9EDB2F0 (17), 0x092072D0E7C9B04E (16),
        // 0x38CCE7E988729817 (15),
        // 0x5DB64D0C91D99368 (14),
        // 0xAEAF871CC4814DA1 (13), 0x9D2695B2F62707D2 (12),
        // 0x12E4F45E48ACEB9B (11), 0x06F71648486D0FEC (10),
        // 0x4F824667CDA7EF25 (9).
        Assert.Equal(0x0D75C57C1848268CUL, sim.StateHash());
    }

    [Fact]
    public void FreshWorld_MatchesFreshWorld()
    {
        GameDatabase database = TestWorld.Database();
        WorldState one = TestWorld.World(database);
        WorldState two = TestWorld.World(database);

        var a = new GameSimulation(database, one, seed: 7);
        var b = new GameSimulation(database, two, seed: 7);

        Assert.Equal(a.StateHash(), b.StateHash());
    }

    private static InputFrame InputAt(int tick) => tick % 7 == 0
        ? InputFrame.Of(new AddManaCommand((byte)(tick % 3), tick))
        : InputFrame.Empty;
}
