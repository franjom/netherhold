using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// What a creature is currently doing.
///
/// Three states, because three is what the tile-work loop needs: look for
/// something to do, walk to it, do it. DK2's own creature state machine is much
/// larger — Flame names dozens of state constants — and reconstructing it is a
/// later job (Q2, Q7). Nothing here claims to be that machine.
/// </summary>
public enum CreatureActivity
{
    /// <summary>Standing still, waiting to re-evaluate what to do.</summary>
    Idle = 0,

    /// <summary>Walking towards a tile it intends to work on.</summary>
    MovingToJob = 1,

    /// <summary>Standing next to its target tile, applying work to it.</summary>
    Working = 2,
}

/// <summary>
/// One creature in the world.
///
/// A class rather than a struct because a creature carries a path, and because
/// creatures are few where tiles are tens of thousands. Stored in a list whose
/// order is spawn order and is never sorted: the order creatures are updated in
/// is behaviour, in exactly the way the work queue's order is (ADR-0010).
///
/// <para>Only the fields some implemented mechanic actually reads are here.
/// Health, mood, anger, experience, hunger and the rest of DK2's creature record
/// arrive with the mechanics that read them, not before — the same discipline
/// <see cref="TileState"/> follows.</para>
/// </summary>
public sealed class CreatureState
{
    /// <summary>Stable identity for the life of the level. Assigned by <see cref="WorldState"/>.</summary>
    public int Id;

    /// <summary>Index into the CREATURES table — the definition this creature is an instance of.</summary>
    public byte CreatureTypeId;

    public byte OwnerPlayerId;

    /// <summary>
    /// Position within the dungeon in <see cref="Fp32"/> units, where
    /// <see cref="Fp32.One"/> is one tile. The centre of tile (x,y) is
    /// <c>(x * 4096 + 2048, y * 4096 + 2048)</c>.
    ///
    /// Sub-tile rather than per-tile because how long a creature takes to reach
    /// its work is gameplay: an imp crossing a dungeon is not teleporting from
    /// tile to tile. Fixed point rather than floating (ADR-0001) so that the
    /// same walk produces the same arrival tick on every machine. Never
    /// negative — the dungeon starts at the origin.
    /// </summary>
    public int PosX;
    public int PosY;

    /// <summary>
    /// Fractional movement carried between ticks, in units of
    /// <c>1 / SimulationTiming.TicksPerSecond</c> of an <see cref="Fp32"/> unit.
    ///
    /// A creature's speed is given per second and spent per tick, and integer
    /// division would throw the remainder away every tick — an imp at 1.84
    /// tiles/second would quietly walk 0.4% slow forever. Keeping the remainder
    /// makes the speed exactly what the data says without leaving integer maths.
    /// </summary>
    public int MoveRemainder;

    public CreatureActivity Activity;

    /// <summary>The tile this creature intends to work on, or -1 for none.</summary>
    public int TargetTileIndex = -1;

    /// <summary>Which kind of work it intends to apply. Meaningless when <see cref="TargetTileIndex"/> is -1.</summary>
    public TileWorkKind TargetWork;

    /// <summary>
    /// Countdown in ticks. What it counts down to depends on
    /// <see cref="Activity"/>: the next re-evaluation when idle, the next work
    /// application when working.
    /// </summary>
    public int TimerTicks;

    /// <summary>
    /// The route being walked, excluding the tile it started from. Empty when
    /// the creature is not going anywhere.
    /// </summary>
    public readonly List<TilePos> Path = [];

    /// <summary>How far along <see cref="Path"/> the creature is.</summary>
    public int PathIndex;

    /// <summary>The tile the creature is standing on.</summary>
    public TilePos Tile => new(PosX / Fp32.One, PosY / Fp32.One);

    /// <summary>The centre of a tile, in position units.</summary>
    public static int CentreOf(int tile) => (tile * Fp32.One) + (Fp32.One / 2);

    public void ClearJob()
    {
        Activity = CreatureActivity.Idle;
        TargetTileIndex = -1;
        TargetWork = default;
        Path.Clear();
        PathIndex = 0;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(CreatureTypeId);
        hash.Add(OwnerPlayerId);
        hash.Add(PosX);
        hash.Add(PosY);
        hash.Add(MoveRemainder);
        hash.Add((int)Activity);
        hash.Add(TargetTileIndex);
        hash.Add((int)TargetWork);
        hash.Add(TimerTicks);
        hash.Add(Path.Count);
        foreach (TilePos p in Path)
        {
            hash.Add(p.X);
            hash.Add(p.Y);
        }
        hash.Add(PathIndex);
    }
}
