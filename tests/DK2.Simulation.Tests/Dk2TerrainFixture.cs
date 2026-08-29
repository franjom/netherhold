using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// The seven terrain types and ten work variables that tile mechanics touch,
/// transcribed from the reference install so the rules can be tested where no
/// Dungeon Keeper 2 installation exists — CI included.
///
/// Unlike <see cref="TestWorld"/>, nothing here is invented: every number is
/// DK2's own, extracted by <c>dk2tools</c> and recorded in
/// docs/research/terrain-room-dump.md and game-database-dump.md. That makes the
/// fixture a liability as well as a convenience — a transcription can drift
/// from the game it claims to copy — so
/// <c>DK2.Behavior.Tests.TileWorkOnRealDataTests</c> re-runs the same
/// assertions against the real install whenever one is present.
/// </summary>
internal static class Dk2TerrainFixture
{
    public const byte ImpenetrableRock = 1;
    public const byte Rock = 2;
    public const byte DirtPath = 3;
    public const byte Gold = 6;
    public const byte Gems = 7;
    public const byte ClaimedPath = 8;
    public const byte ReinforcedWall = 9;

    public static GameDatabase Database() => new(
        [
            T(ImpenetrableRock, "Impenetrable Rock",
              TerrainFlags.Solid | TerrainFlags.Impenetrable, 1, 1, 1, 1),
            T(Rock, "Rock",
              TerrainFlags.Solid | TerrainFlags.Taggable, 165, 660, ReinforcedWall, DirtPath),
            T(DirtPath, "Dirt Path",
              TerrainFlags.FillInable, 1, 660, ClaimedPath, DirtPath),
            T(Gold, "Gold",
              TerrainFlags.Solid | TerrainFlags.Taggable, 660, 660, Gold, DirtPath, goldValue: 3000),
            T(Gems, "Gems",
              TerrainFlags.Solid | TerrainFlags.Impenetrable | TerrainFlags.Taggable,
              100, 100, Gems, Gems, goldValue: 3000),
            T(ClaimedPath, "Claimed Path",
              TerrainFlags.Ownable | TerrainFlags.Attackable | TerrainFlags.FillInable,
              660, 660, Rock, DirtPath, manaGain: 1),
            T(ReinforcedWall, "Reinforced Wall",
              TerrainFlags.Solid | TerrainFlags.Ownable | TerrainFlags.Taggable | TerrainFlags.Attackable,
              900, 900, ReinforcedWall, Rock, manaGain: 1),
        ],
        rooms: [],
        creatures: [],
        variables: Variables());

    /// <summary>The ten tile-work health deltas plus the gems yield, as shipped.</summary>
    public static GlobalVariables Variables()
    {
        var vars = new GlobalVariables();
        Set(vars, VariableTypeId.ClaimTileHealth, 825);
        Set(vars, VariableTypeId.AttackTileHealth, -140);
        Set(vars, VariableTypeId.RepairTileHealth, 125);
        Set(vars, VariableTypeId.MineGoldHealth, -300);
        Set(vars, VariableTypeId.DigRockHealth, -5000);
        Set(vars, VariableTypeId.DigOwnWallHealth, -130);
        Set(vars, VariableTypeId.DigEnemyWallHealth, -8);
        Set(vars, VariableTypeId.FillInHealth, 5);
        Set(vars, VariableTypeId.ReinforceWallHealth, 50);
        Set(vars, VariableTypeId.RepairWallHealth, 22);
        Set(vars, VariableTypeId.GoldMinedFromGems, 25);
        return vars;
    }

    private static void Set(GlobalVariables vars, VariableTypeId id, int value) =>
        vars.Misc[id] = new MiscVariable(id, value, 0, 0);

    private static TerrainDef T(
        byte id, string name, TerrainFlags flags,
        ushort startingHealth, ushort maxHealth,
        byte maxHealthType, byte destroyedType,
        ushort goldValue = 0, ushort manaGain = 0) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = startingHealth,
        MaxHealth = maxHealth,
        GoldValue = goldValue,
        ManaGain = manaGain,
        MaxManaGain = 0,
        Damage = 0,
        MaxHealthTypeTerrainId = maxHealthType,
        DestroyedTypeTerrainId = destroyedType,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };

    /// <summary>
    /// A small dungeon: impenetrable border, a rock body, a claimed-path room
    /// owned by player 1 at the top-left, one gold seam and one gem face.
    /// </summary>
    public static LevelMap Map(int width = 16, int height = 12)
    {
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool room = x is >= 1 and <= 3 && y is >= 1 and <= 3;

                byte terrain = border ? ImpenetrableRock : room ? ClaimedPath : Rock;
                byte owner = (byte)(room ? 1 : 0);
                tiles[(y * width) + x] = new MapTile(terrain, owner, 0, 0);
            }
        }

        tiles[(5 * width) + 5] = new MapTile(Gold, 0, 0, 0);
        tiles[(5 * width) + 6] = new MapTile(Gems, 0, 0, 0);
        return new LevelMap(width, height, tiles);
    }

    public static WorldState World() =>
        WorldState.FromMap(Database(), Map(), [(byte)0, (byte)1, (byte)2]);

    /// <summary>A simulation with the real M3 tile systems, in registration order.</summary>
    public static GameSimulation Simulation(ulong seed = 4242)
    {
        GameDatabase database = Database();
        return new GameSimulation(
            database,
            WorldState.FromMap(database, Map(), [(byte)0, (byte)1, (byte)2]),
            seed,
            [new Systems.TileTagSystem(), new Systems.TileWorkSystem()]);
    }
}
