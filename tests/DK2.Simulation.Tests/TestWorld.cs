using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// A synthetic database and map, so the determinism gate runs everywhere —
/// including CI, where no Dungeon Keeper 2 installation exists.
///
/// The values here are invented on purpose and are NOT claims about DK2. They
/// exist only to give the simulation loop something structurally realistic to
/// chew on. Tests that assert real game behaviour load the real database and
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
        creatures: [],
        variables: new GlobalVariables());

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

    /// <summary>A simulation with the chaos system attached, for gate testing.</summary>
    public static GameSimulation Simulation(ulong seed = 12345)
    {
        GameDatabase database = Database();
        return new GameSimulation(database, World(database), seed, [new ChaosSystem()]);
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
