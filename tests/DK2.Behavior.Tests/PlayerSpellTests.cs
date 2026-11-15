// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Every offered spell and the cast action (campaign bot stage 3 plan, Task 4) on the shipped
/// spell definitions: the Spell panel's offers with their price and target, and a cast resolved to the
/// command the click sends (<c>PlacementCursor.Spell</c>).</summary>
public class PlayerSpellTests
{
    private static readonly PlayerId Keeper = new(3);
    private static readonly PlayerRunId Run = new("spell-test");

    private sealed record Setup(GameDatabase Database, WorldState World, CreatureState Imp);

    /// <summary>Level 1's start with Heal, Thunderbolt, Call to Arms and Create Imp available and mana to cast them.</summary>
    private static Setup Level1(int mana = 100_000)
    {
        GameDatabase database = ScenarioCorpus.Database;
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "level1")!;
        WorldState world = WorldState.FromMap(database, map, map.Tiles.Select(t => t.PlayerId).Append(Keeper.Value).Distinct());
        foreach (byte spell in (byte[])[KeeperSpellRules.Heal, KeeperSpellRules.Thunderbolt, CallToArmsRules.SpellId, BottomPanelView.CreateImpSpellId])
            world.SetAvailability(Keeper.Value, AvailabilityKind.Spell, spell, 3);
        world.Player(Keeper).Mana = mana;
        int own = Enumerable.Range(0, map.Width * map.Height).First(i =>
            world.Tile(i % map.Width, i / map.Width).OwnerPlayerId == Keeper.Value && database.RoomByTerrain(world.Tile(i % map.Width, i / map.Width).TerrainId) is null
            && (database.TerrainById(world.Tile(i % map.Width, i / map.Width).TerrainId).Flags & TerrainFlags.Solid) == 0);
        CreatureState imp = world.SpawnCreature(database, 1, Keeper.Value, new TilePos(own % map.Width, own / map.Width));
        return new Setup(database, world, imp);
    }

    private static PlayerObservationCapture Capture(Setup s) =>
        new PlayerObservationBuilder(s.Database, Keeper).Capture(s.World, new PlayerStamp(Run, Keeper, s.World.CurrentTick, new PlayerRevision(1)), []);

    [InstallFact]
    public void TheSpellPanelsOffersCarryPriceAffordabilityAndTarget()
    {
        Setup s = Level1();
        PlayerObservation seen = Capture(s).Observation;
        PlayerOffers offers = seen.Offers;
        PlayerSpellOffer heal = Assert.Single(offers.Spells, o => o.Spell == KeeperSpellRules.Heal);
        PlayerSpellOffer call = Assert.Single(offers.Spells, o => o.Spell == CallToArmsRules.SpellId);
        Assert.True(heal.OnCreature);
        Assert.True(Assert.Single(offers.Spells, o => o.Spell == KeeperSpellRules.Thunderbolt).OnCreature);
        Assert.False(call.OnCreature);
        Assert.True(heal.Affordable);
        Assert.Equal(s.Database.KeeperSpellById(KeeperSpellRules.Heal)!.Name, heal.Name);
        Assert.Contains(offers.Spells, o => o.Spell == BottomPanelView.CreateImpSpellId);
        Assert.True((offers.Actions & PlayerActionKinds.CastSpell) != 0);

        Setup poor = Level1(mana: 0);
        Assert.False(Assert.Single(Capture(poor).Observation.Offers.Spells, o => o.Spell == KeeperSpellRules.Heal).Affordable);
    }

    [InstallFact]
    public void TheObservationCarriesTheManaOrbsFiguresAndAnUpgradedSpell()
    {
        Setup s = Level1();
        PlayerObservation seen = Capture(s).Observation;
        KeeperResourceSnapshot orb = new KeeperResourceView(s.Database, Keeper).Capture(s.World);
        Assert.Equal(orb.Income, seen.ManaIncome);
        Assert.Equal(orb.Upkeep, seen.ManaUpkeep);
        Assert.True(seen.ManaIncome.Value > 0, "Level 1's heart earns mana");
        Assert.True(seen.ManaUpkeep.Value > 0, "the Imp costs upkeep");

        Assert.False(Assert.Single(seen.Offers.Spells, o => o.Spell == BottomPanelView.CreateImpSpellId).Upgraded);
        s.World.SetUpgraded(Keeper, AvailabilityKind.Spell, BottomPanelView.CreateImpSpellId, true);
        Assert.True(Assert.Single(Capture(s).Observation.Offers.Spells, o => o.Spell == BottomPanelView.CreateImpSpellId).Upgraded);
    }

    [InstallFact]
    public void PossessionIsNotOfferedAndAJustCastSpellIsNotReady()
    {
        Setup s = Level1();
        s.World.SetAvailability(Keeper.Value, AvailabilityKind.Spell, PossessionRules.SpellId, 3);
        Assert.DoesNotContain(Capture(s).Observation.Offers.Spells, o => o.Spell == PossessionRules.SpellId);   // no action ends one

        // A shipped spell with a recharge, cast a tick ago: its slot's pie is not done.
        byte recharging = (byte)s.Database.KeeperSpells.Values.First(k => k.CooldownRaw > 0
            && k.KeeperSpellId is not (PossessionRules.SpellId or 5 or 14)).KeeperSpellId;
        s.World.SetAvailability(Keeper.Value, AvailabilityKind.Spell, recharging, 3);
        PlayerSpellOffer offered = Assert.Single(Capture(s).Observation.Offers.Spells, o => o.Spell == recharging);
        Assert.True(offered.Ready);
        List<AvailabilityState> rows = s.World.Player(Keeper).Availabilities;
        int at = rows.FindIndex(r => r.Kind == AvailabilityKind.Spell && r.TypeId == offered.Spell);
        rows[at] = rows[at] with { LastCastTick = s.World.CurrentTick.Value + 1 };
        Assert.False(Assert.Single(Capture(s).Observation.Offers.Spells, o => o.Spell == offered.Spell).Ready);
    }

    [InstallFact]
    public void ACastResolvesToTheCommandTheClickSendsAndRefusesTheWrongTarget()
    {
        Setup s = Level1();
        PlayerObservationCapture capture = Capture(s);
        var adapter = new PlayerActionAdapter(s.Database, Keeper);
        PlayerThingHandle imp = Assert.Single(capture.Observation.Creatures).Handle!.Value;
        TilePos tile = s.Imp.Tile;

        PlayerCommand heal = Assert.Single(adapter.Resolve(capture, new PlayerAction.CastSpell(KeeperSpellRules.Heal, Target: imp)).Commands);
        Assert.Equal(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, s.Imp.Id), heal);
        PlayerCommand call = Assert.Single(adapter.Resolve(capture, new PlayerAction.CastSpell(CallToArmsRules.SpellId, Tile: tile)).Commands);
        Assert.Equal(new CastSpellAtPlaceCommand(Keeper, CallToArmsRules.SpellId, CreatureState.CentreOf(tile.X), CreatureState.CentreOf(tile.Y)), call);

        PlayerRefusalReason Refusal(PlayerAction action) => adapter.Resolve(capture, action).Refusal;
        Assert.Equal(PlayerRefusalReason.NotOffered, Refusal(new PlayerAction.CastSpell(KeeperSpellRules.Heal, Tile: tile)));               // a creature spell at a place
        Assert.Equal(PlayerRefusalReason.NotOffered, Refusal(new PlayerAction.CastSpell(CallToArmsRules.SpellId, Target: imp)));           // a place spell on a creature
        Assert.Equal(PlayerRefusalReason.NotOffered, Refusal(new PlayerAction.CastSpell(KeeperSpellRules.Heal, imp, tile)));                // both
        Assert.Equal(PlayerRefusalReason.NotOffered, Refusal(new PlayerAction.CastSpell(KeeperSpellRules.Inferno, Tile: tile)));            // not on the panel
        Assert.Equal(PlayerRefusalReason.NotOffered, Refusal(new PlayerAction.CastSpell(BottomPanelView.CreateImpSpellId, Tile: tile)));    // has its own action
        Assert.Equal(PlayerRefusalReason.OutOfBounds, Refusal(new PlayerAction.CastSpell(CallToArmsRules.SpellId, Tile: new TilePos(-1, 0))));
        Assert.Equal(PlayerRefusalReason.UnknownHandle, Refusal(new PlayerAction.CastSpell(KeeperSpellRules.Heal,
            Target: new PlayerThingHandle(capture.Observation.Stamp.Revision, 999))));
    }
}
