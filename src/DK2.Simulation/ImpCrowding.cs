using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// How many workers fit on one face of a block, and what the ones who do not fit
/// do about it.
///
/// <para><b>CONFIRMED by observation of the original</b> (2026-08-29, owner,
/// Q26/Q43): <b>three imps to a face and twelve to a block</b>, and — the half
/// that matters far more — an imp that cannot get onto a face <b>goes and finds
/// other work</b>. It does not queue. So imps spread around a block face by face
/// and then overflow into different jobs entirely, which is why a DK2 dungeon
/// makes progress on several fronts at once instead of piling onto the nearest
/// marked tile.</para>
///
/// <para><b>The twelve is not a rule.</b> It is the three, four times: a tile has
/// four orthogonal neighbours and work is orthogonal (Q27, and the twelve is how
/// the owner established that — twelve at three a face is four faces, which
/// eight-connected work could not give). Nothing here counts to twelve.</para>
///
/// <para><b>The three is an observed number with no source in the data</b>, and
/// that is a second and different category from
/// <see cref="ObservedCorrections"/>. There the file carries a value that cannot
/// produce the observed behaviour; here the file carries no value at all. The
/// balance table was searched: it has no per-face, per-tile or crowding limit for
/// workers of any kind, and the nearest thing to one —
/// <c>OvercrowdingJailBreakPercentagePerExtraCreature</c> — is about prisons. It
/// is not derivable from creature geometry either: the CREATURES record gives a
/// creature a <see cref="CreatureDef.Height"/> and no width or radius, so
/// "how many fit along an edge" cannot be computed from what we parse.</para>
///
/// <para>The same bar applies as for a correction: the behaviour is measured
/// rather than inferred, and the data was searched before the constant was
/// written. Q34's 50% sell refund is the other known member of this category and
/// will land here when selling is implemented.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imp-tasks.md")]
public static class ImpCrowding
{
    /// <summary>
    /// How many workers may work one tile from the same neighbouring tile:
    /// <b>three</b>. Observed in the original; in no file.
    /// </summary>
    public const int MaxWorkersPerFace = 3;

    /// <summary>
    /// Whether <paramref name="face"/> already has its full complement of
    /// workers on <paramref name="targetTileIndex"/>.
    ///
    /// <para>A worker's face is where it will be standing when it swings — the
    /// end of its route if it is still walking, or the tile under it if it has
    /// arrived. Counting the intended face rather than the current position is
    /// what stops four imps setting off for the same side of a block and only
    /// discovering the crowd on arrival.</para>
    ///
    /// <para>Counted across every player, not per keeper: the limit is physical
    /// space on a tile, and two keepers' imps digging the same block from the
    /// same side are in each other's way exactly as much as one keeper's would
    /// be. <paramref name="askingCreatureId"/> is excluded so that a worker
    /// re-evaluating the job it already holds does not count itself out of
    /// it.</para>
    /// </summary>
    public static bool FaceIsFull(
        WorldState world,
        GameDatabase database,
        int targetTileIndex,
        TilePos face,
        int askingCreatureId)
    {
        int faceIndex = (face.Y * world.Width) + face.X;
        int count = 0;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.Id == askingCreatureId) continue;
            if (creature.TargetTileIndex != targetTileIndex) continue;
            if (FaceOf(world, creature) != faceIndex) continue;
            if (!database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
                continue;

            if (++count >= MaxWorkersPerFace) return true;
        }

        return false;
    }

    /// <summary>
    /// The tile a worker intends to swing from: the end of its route, or the
    /// tile it stands on when it has none.
    ///
    /// Derived rather than stored, so it cannot disagree with the route the
    /// creature is actually walking — one less field to keep in step, and one
    /// less field in the determinism hash.
    /// </summary>
    public static int FaceOf(WorldState world, CreatureState creature)
    {
        TilePos at = creature.Path.Count > 0 ? creature.Path[^1] : creature.Tile;
        return (at.Y * world.Width) + at.X;
    }
}
