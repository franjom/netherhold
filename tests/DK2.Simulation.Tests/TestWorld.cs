// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// A synthetic database and map, so the determinism gate runs everywhere —
/// including CI, where no Dungeon Keeper 2 installation exists.
///
/// The terrain and the map here are invented on purpose and are NOT claims
/// about DK2; they exist only to give the simulation loop something
/// structurally realistic to chew on. The three groups whose comments say
/// otherwise — variables, keeper spells, creatures — are DK2's own, because
/// those reach the determinism hash. Tests that assert real game behaviour load the real database and
/// skip when the install is absent.
/// </summary>
internal static class TestWorld
{
    public const byte Impenetrable = 1;
    public const byte Rock = 2;
    public const byte DirtPath = 3;
    public const byte ClaimedPath = 8;

    public static GameDatabase Database() => new(
        [
            Terrain(Impenetrable, "Impenetrable Rock", TerrainFlags.Solid | TerrainFlags.Impenetrable, 0),
            Terrain(Rock, "Rock", TerrainFlags.Solid | TerrainFlags.Taggable, 165,
                    maxHealthType: 9, destroyedType: DirtPath),
            Terrain(DirtPath, "Dirt Path", TerrainFlags.Ownable, 100, maxHealthType: ClaimedPath),
            Terrain(ClaimedPath, "Claimed Path", TerrainFlags.Ownable, 100),
            Terrain(9, "Reinforced Wall", TerrainFlags.Solid, 900, destroyedType: Rock),
        ],
        rooms: [],

        // The two creature records are DK2's own, borrowed from Dk2TerrainFixture
        // rather than invented like the terrain above, for the reason the variables
        // below are: they reach the determinism hash through Hp, and an invented
        // number inside a pinned hash is one nobody can check. Borrowed rather than
        // copied because that fixture's *OnRealDataTests re-read both out of the
        // shipped file, so neither can drift from the game.
        creatures: [Dk2TerrainFixture.Imp(), Dk2TerrainFixture.Goblin()],
        variables: Variables(),
        keeperSpells: KeeperSpells());

    /// <summary>
    /// The keeper spells a registered system reads. Only Create Imp, because
    /// only Create Imp is read: <see cref="ImpSupplyRules"/> takes its cast cost
    /// and <see cref="ManaRules"/> takes the per-second upkeep every imp then
    /// charges its keeper.
    ///
    /// <para>Both numbers are DK2's own — 1500 to cast, 7 a second to keep — for
    /// the same reason the variables below are: a made-up figure here would be a
    /// made-up figure inside a determinism hash. `KeeperSpellsOnRealDataTests`
    /// reads both back out of the shipped file so these cannot drift from it.</para>
    /// </summary>
    private static KeeperSpellDef[] KeeperSpells() =>
    [
        new() { Name = KeeperSpellName.CreateImp, ManaCost = 1500, ManaUpkeepPerSecond = 7 },
    ];

    /// <summary>
    /// Only what a system would throw without. `GameDatabase` refuses to invent
    /// a missing balance value, so a synthetic database has to carry an entry
    /// for every variable the registered systems read - three for
    /// <see cref="ManaSystem"/> and two for
    /// <see cref="Systems.ImpSupplySystem"/>. The numbers are DK2's, because a
    /// made-up threshold here would be a made-up number in a determinism hash;
    /// no terrain in this fixture generates mana, and neither system that reads
    /// them is registered — <see cref="Simulation"/> attaches the chaos system
    /// and nothing else, so the Imp below never supplies anybody.
    /// </summary>
    private static GlobalVariables Variables()
    {
        var vars = new GlobalVariables();
        Set(vars, VariableTypeId.MaximumManaThreshold, 200000);
        Set(vars, VariableTypeId.MaximumManaGainPerSecond, 500);
        Set(vars, VariableTypeId.ExcessManaDecreaseRatePerSecond, 5);
        // The low-mana warning threshold and the warning/scan interval, as shipped (ADR-0277).
        Set(vars, VariableTypeId.LowManaWarningThreshold, 100);
        Set(vars, VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds, 10);
        // Payday, as shipped: ten minutes between them, and the first one ten
        // minutes in rather than at zero (Q9, ADR-0078).
        Set(vars, VariableTypeId.PayDayFrequencySeconds, 600);
        Set(vars, VariableTypeId.TimeBeforeFirstPayDaySeconds, 600);
        Set(vars, VariableTypeId.MinimumImpThreshold, 4);
        Set(vars, VariableTypeId.TimeBeforeFreeImpGeneratedSeconds, 5);
        // The dungeon heart's health and its regeneration, as shipped in the global
        // VARIABLES table (balance-variables-in-the-binary.md rows 174 and 131). A
        // keeper's world cannot be built without them (ADR-0162).
        Set(vars, VariableTypeId.DungeonHeartObjectHealth, 100000);
        Set(vars, VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25);
        return vars;
    }

    private static void Set(GlobalVariables vars, VariableTypeId id, int value) =>
        vars.Misc[id] = new MiscVariable(id, value, 0, 0);

    private static TerrainDef Terrain(
        byte id, string name, TerrainFlags flags, ushort health,
        byte maxHealthType = 0, byte destroyedType = 0) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = health,
        MaxHealth = health,
        GoldValue = 0,
        ManaGain = 0,
        MaxManaGain = 0,
        Damage = 0,
        MaxHealthTypeTerrainId = maxHealthType,
        DestroyedTypeTerrainId = destroyedType,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };

    /// <summary>
    /// A walled room of dirt path: impenetrable border, rock ring, dirt inside.
    /// Deterministic — no randomness in its construction.
    /// </summary>
    public static LevelMap Map(int width = 24, int height = 20)
    {
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool ring = x == 1 || y == 1 || x == width - 2 || y == height - 2;
                byte terrain = border ? Impenetrable : ring ? Rock : DirtPath;
                byte owner = (byte)(terrain == DirtPath ? 1 : 0);
                tiles[(y * width) + x] = new MapTile(terrain, owner, 0, 0);
            }
        }
        return new LevelMap(width, height, tiles);
    }

    public static WorldState World(GameDatabase database) =>
        WorldState.FromMap(database, Map(), [(byte)0, (byte)1, (byte)2]);

    /// <summary>A simulation with the chaos system attached, for gate testing.
    /// <para><b>Two creatures stand in it</b>, which is the point of them: until
    /// 2026-09-10 the pinned world had none, so <c>CreatureState.HashInto</c> never
    /// ran inside the gate and a change to it could not move the pinned literal
    /// (Q206 — verified by hashing a constant there and watching the pin pass).
    /// Two rather than one, of different types and different owners, so the
    /// <see cref="WorldState.Creatures"/> order is hashed as well as the fields:
    /// that order is behaviour (ADR-0010).</para>
    /// <para>Here rather than in <see cref="World"/> because this is the gate's
    /// fixture and that one is shared: five other files build worlds from it and
    /// none of them asked for a population.</para></summary>
    public static GameSimulation Simulation(ulong seed = 12345)
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, ownerPlayerId: 1, new TilePos(3, 3));
        world.SpawnCreature(
            database, Dk2TerrainFixture.GoblinCreatureId, ownerPlayerId: 2, new TilePos(5, 4));
        return new GameSimulation(database, world, seed, [new ChaosSystem()]);
    }
}

/// <summary>
/// A test-only system that consumes randomness and mutates world state every
/// tick. It implements no DK2 mechanic and never will.
///
/// Its job is to keep the determinism gate honest. With no systems registered
/// the world never changes, so "both runs agree" would be trivially true and
/// the gate would pass even if the loop, the hash or the snapshot were broken.
/// This makes state move in a way that is reproducible only if everything
/// underneath really is deterministic.
/// </summary>
internal sealed class ChaosSystem : ISimulationSystem
{
    public string Name => "chaos";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;

        // Damage a random tile.
        int index = context.Rng.NextInt(world.TileCount);
        ref TileState tile = ref world.TileAtIndex(index);
        tile.Health -= context.Rng.NextInt(1, 10);
        if (tile.Health < 0) tile.Health = 0;

        // Move mana between two random players. Mana rather than gold because gold
        // is not a player-level number - it lives on tiles (GoldStore) - and a
        // chaos system that wrote one would be writing to a field the game does
        // not have.
        int from = context.Rng.NextInt(world.PlayerCount);
        int to = context.Rng.NextInt(world.PlayerCount);
        int amount = context.Rng.NextInt(1, 25);
        world.PlayerAtIndex(from).Mana -= amount;
        world.PlayerAtIndex(to).Mana += amount;

        // Move a creature, so the gate covers CreatureState.HashInto changing
        // and not merely running (Q206). Both writes are bounded by
        // construction: the nudge is relative to the creature's own tile centre
        // and half a tile is 2048, so the position stays inside that tile and
        // never goes negative, and hunger only climbs. Which creature is picked
        // is itself part of what has to come out the same twice.
        CreatureState creature = world.Creatures[context.Rng.NextInt(world.Creatures.Count)];
        creature.PosX = CreatureState.CentreOf(creature.Tile.X) + context.Rng.NextInt(-2047, 2047);
        creature.PosY = CreatureState.CentreOf(creature.Tile.Y) + context.Rng.NextInt(-2047, 2047);
        creature.HungerNeed += context.Rng.NextInt(1, 5);

        // Commands are applied verbatim so input differences show up in state.
        foreach (PlayerCommand command in context.Input.All)
            if (command is AddManaCommand mana && world.HasPlayer(mana.PlayerId))
                world.Player(mana.PlayerId).Mana += mana.Amount;

        context.Emit(Name, "tile-damaged", $"index={index} health={tile.Health}");
    }
}

/// <summary>Test-only command, to exercise the input path of the gate.</summary>
internal sealed record AddManaCommand(byte PlayerId, int Amount) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Amount);
    }
}
