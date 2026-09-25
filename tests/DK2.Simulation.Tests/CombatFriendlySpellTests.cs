// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The friendly spell arm — docs/research/spell-friendly-arm.md, ADR-0170: the survey keeps
/// the last castable friendly slot; Invisible is cast on the caster alone; every other buff goes to
/// the caster's nearest own creature in the Combat category under a quarter health that wants it;
/// the cast is made once and the target let go. The fixture Priest carries Haste, Cast Armour and
/// Heal; the Thief carries Invisible. Enemies are frozen where they must stand and not hit back.</summary>
public class CombatFriendlySpellTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void APriestHealsItsOwnFighterUnderAQuarterHealth_TheLastEligibleSlotFirst()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState priest = Spawn(db, world, Dk2TerrainFixture.PriestCreatureId, Keeper, 5, 10);
        // A fearless fighter: under a quarter health is under a third, and anyone else would withdraw.
        CreatureState fighter = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Keeper, 8, 10);
        Frozen(Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, fighter));
        fighter.Health = 300;
        int max = CreatureHealthRules.MaxHealth(db, db.CreatureById(fighter.CreatureTypeId), fighter);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(44);
        // The Priest's first ally engagement: nibble 3 and the Heal slot, the last friendly slot that is
        // castable; no attacker slot is taken on the ally.
        SimEvent engaged = sink.Events.First(e => e.Kind == "engaged" && e.Detail.Contains("creature=" + priest.Id) && e.Detail.Contains("ally="));
        Assert.Contains("ally=" + fighter.Id + " attack=3 slot=2", engaged.Detail);
        Assert.DoesNotContain(priest.Id, fighter.EngagedRanged);
        SimEvent cast = sink.Events.First(e => e.Kind == "casts_on_ally" && e.Detail.Contains("creature=" + priest.Id));
        Assert.Contains("slot=2", cast.Detail);
        // Cast once: the target is let go the same tick, and the Priest never hurts its ally.
        Assert.DoesNotContain(sink.Events, e => e.Kind == "hit" && e.Detail.Contains("creature=" + priest.Id) && e.Detail.Contains("target=" + fighter.Id));
        SimEvent healed = sink.Events.First(e => e.Kind == "healed" && e.Detail.Contains("target=" + fighter.Id));
        // min(3000, max − 300): the heal is capped at the missing health.
        Assert.Contains("amount=" + (max - 300), healed.Detail);
        Assert.Equal(max, fighter.Health);
        // The frozen enemy takes nothing from the Heal chain, and the ally was never told (no retaliation event on it).
        Assert.DoesNotContain(sink.Events, e => e.Kind == "shot_hit");
    }

    [Fact]
    public void WithHealSpent_ArmourIsCastNext_ThenHaste_AllOnTheHurtFighter()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState priest = Spawn(db, world, Dk2TerrainFixture.PriestCreatureId, Keeper, 5, 10);
        priest.SpellStamps[2] = 100000; // Heal on cooldown for the whole test
        CreatureState fighter = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Keeper, 8, 10);
        Frozen(Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, fighter));
        fighter.Health = 300;
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(60);
        long[] slots = sink.Events.Where(e => e.Kind == "casts_on_ally" && e.Detail.Contains("creature=" + priest.Id))
            .Select(e => long.Parse(e.Detail.Split("slot=")[1])).ToArray();
        Assert.True(slots.Length >= 2, "two casts: " + string.Join(",", slots));
        Assert.Equal(1, slots[0]); // Cast Armour, the last castable friendly slot with Heal spent
        Assert.Equal(0, slots[1]); // then Haste, once Armour's cooldown runs and the fighter is still under a quarter
        // The Armour shot, 2048 a tick, cannot miss a standing ally; the Haste shot, 3686 a tick, samples
        // positions further apart than the overlap box is wide and may pass it (shots.md §4a, Q401).
        Assert.True(fighter.ArmourTicks > 0, "the Armour landed");
        Assert.Contains(sink.Events, e => e.Kind == "shot_fired" && e.Detail.Contains("spell=" + Dk2TerrainFixture.HasteSpellId));
    }

    [Fact]
    public void AThiefInAFightCastsInvisibleOnItself_AndItsOwnShotLandsOnIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState thief = Spawn(db, world, Dk2TerrainFixture.ThiefCreatureId, Keeper, 5, 10);
        CreatureState enemy = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 7, 10);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(20);
        SimEvent cast = sink.Events.First(e => e.Kind == "casts_on_ally" && e.Detail.Contains("creature=" + thief.Id));
        Assert.Contains("ally=" + thief.Id, cast.Detail);
        // Sixty ticks from the cast, counting down since.
        SimEvent invisible = sink.Events.First(e => e.Kind == "invisible" && e.Detail.Contains("creature=" + thief.Id));
        Assert.Contains("ticks=60", invisible.Detail);
        Assert.InRange(thief.InvisibleTicks, 1, 60);
        Assert.False(CombatRules.IsAttackable(thief));
        Assert.NotEqual(thief.Id, enemy.FightTargetId);
    }

    [Fact]
    public void AFighterAtHalfHealthOrMore_IsNoFriendlyTarget_ButStillWantsAHeal()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState priest = Spawn(db, world, Dk2TerrainFixture.PriestCreatureId, Keeper, 5, 10);
        CreatureState fighter = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        Frozen(Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, fighter));
        fighter.Health = 700; // under half, over a quarter
        CreatureDef goblin = db.CreatureById(fighter.CreatureTypeId);
        CreatureSpellDef heal = db.CreatureSpells[Dk2TerrainFixture.HealSpellId];
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(30);
        Assert.True(SpellRules.BuffApplies(db, goblin, fighter, heal), "the Heal predicate alone would accept half health");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "casts_on_ally");
        Assert.NotEqual(CombatRules.AttackTypes.SpellAtAlly, priest.AttackType);
    }

    [Fact]
    public void TheApplicabilityTable_ByAlternativeShot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        CreatureDef def = db.CreatureById(goblin.CreatureTypeId);
        CreatureSpellDef haste = db.CreatureSpells[Dk2TerrainFixture.HasteSpellId];
        CreatureSpellDef armour = db.CreatureSpells[Dk2TerrainFixture.ArmourSpellId];
        CreatureSpellDef heal = db.CreatureSpells[Dk2TerrainFixture.HealSpellId];
        CreatureSpellDef fireball = db.CreatureSpells[Dk2TerrainFixture.FireballSpellId];

        Assert.True(SpellRules.BuffApplies(db, def, goblin, haste));
        goblin.SpeedEffectTicks = 5;
        Assert.False(SpellRules.BuffApplies(db, def, goblin, haste));

        // Armour wants the Combat category — a walk to a fight counts — and a spent word.
        Assert.False(SpellRules.BuffApplies(db, def, goblin, armour));
        StatePlacement.As(goblin, CreatureActivity.Fighting);
        Assert.True(SpellRules.BuffApplies(db, def, goblin, armour));
        goblin.ArmourTicks = 1;
        Assert.False(SpellRules.BuffApplies(db, def, goblin, armour));
        StatePlacement.As(goblin, CreatureActivity.Celebrating);
        goblin.ArmourTicks = 0;
        Assert.True(SpellRules.BuffApplies(db, def, goblin, armour));

        // Heal: strictly under half the level-scaled maximum, in any state.
        StatePlacement.As(goblin, CreatureActivity.Idle);
        goblin.Health = 750;
        Assert.False(SpellRules.BuffApplies(db, def, goblin, heal));
        goblin.Health = 749;
        Assert.True(SpellRules.BuffApplies(db, def, goblin, heal));

        // An attack spell indexes record 0: never applicable.
        Assert.False(SpellRules.BuffApplies(db, def, goblin, fireball));
    }

    [Fact]
    public void TheLastCastableFriendlySlotWins()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState priest = Spawn(db, world, Dk2TerrainFixture.PriestCreatureId, Keeper, 5, 10);
        CreatureDef def = db.CreatureById(priest.CreatureTypeId);

        Assert.Equal(2, SpellRules.SurveyFriendly(db, priest, def, 10, out CreatureSpellDef? chosen));
        Assert.Equal(Dk2TerrainFixture.HealSpellId, chosen!.CreatureSpellId);
        priest.SpellStamps[2] = 9;
        Assert.Equal(1, SpellRules.SurveyFriendly(db, priest, def, 10, out chosen));
        Assert.Equal(Dk2TerrainFixture.ArmourSpellId, chosen!.CreatureSpellId);
        priest.SpellStamps[1] = 9;
        Assert.Equal(0, SpellRules.SurveyFriendly(db, priest, def, 10, out _));
        priest.SpellStamps[0] = 9;
        Assert.Equal(-1, SpellRules.SurveyFriendly(db, priest, def, 10, out chosen));
        Assert.Null(chosen);
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static CreatureState Touching(GameDatabase db, WorldState world, byte type, byte owner, CreatureState other)
    {
        CreatureState c = Spawn(db, world, type, owner, other.Tile.X, other.Tile.Y);
        c.PosX = other.PosX + (Fp32.One / 3);
        c.PosY = other.PosY;
        return c;
    }

    private static CreatureState Frozen(CreatureState c)
    {
        c.FrozenTicks = 10000;
        StatePlacement.As(c, CreatureActivity.Frozen);
        return c;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CreatureStatusSystem(), new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
}
