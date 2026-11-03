// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Where a world voice's thing is now. DK2 replays a thing's live handle at the thing's position on every update of the thing (slot
/// <c>+0x30</c>: <c>Creature_Update</c> <c>0x0048A86E</c>, <c>Object_Update</c> <c>0x0049BE87</c>, <c>Shot_Update</c> <c>0x004AB7C3</c>; a door,
/// a trap and a room's tick replay a position that never changes), which moves the playing voice without restarting it. Ours keeps no per-update
/// event for it: the listener asks where the voice's thing is when it draws a frame, so no work is done while nothing plays. Presentation only:
/// it reads the world (docs/research/sound-effects-continuous.md section 3).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#3-looping-thing-and-room-sounds")]
public static class VoiceLocator
{
    /// <summary>Whether the thing keeps its place (<see cref="Static"/>, or a voice that names no world thing), has moved to the returned
    /// place (<see cref="Moves"/>), or is no longer in the world (<see cref="Gone"/>).</summary>
    public enum Fate { Static, Moves, Gone }

    /// <summary>The position in the view's space, <c>DungeonScene.Ground</c>'s (x, height, -y) in tiles; <paramref name="height"/> is null where the
    /// thing keeps its own and the voice keeps the height it was asked at.</summary>
    public static Fate Locate(WorldState world, SoundVoiceId voice, out float x, out float? height, out float z)
    {
        x = z = 0;
        height = null;
        if ((voice.Value & 0x80000000u) == 0) return Fate.Static;
        var kind = (SoundSourceKind)((voice.Value >> 24) & 0x7F);
        int id = (int)(voice.Value & 0x00FFFFFFu);
        switch (kind)
        {
            case SoundSourceKind.Creature:
                if (world.CreatureById(id) is not { } creature) return Fate.Gone;
                (x, height, z) = (creature.PosX / 4096f, creature.PosZ / 4096f, -creature.PosY / 4096f);
                return Fate.Moves;
            case SoundSourceKind.Object:
                if (world.ObjectById(id) is not { } thing) return Fate.Gone;
                bool placed = thing.PosX != 0 || thing.PosY != 0;
                x = (placed ? thing.PosX : CreatureState.CentreOf(thing.TileIndex % world.Width)) / 4096f;
                z = -(placed ? thing.PosY : CreatureState.CentreOf(thing.TileIndex / world.Width)) / 4096f;
                return Fate.Moves;
            case SoundSourceKind.Shot:
            case SoundSourceKind.ShotSecond:
                if (world.ShotById(id) is not { } shot) return Fate.Gone;
                FpPosition3 at = shot.ViewPosition;
                (x, height, z) = (at.X.Raw / 4096f, at.Z.Raw / 4096f, -at.Y.Raw / 4096f);
                return Fate.Moves;
            default:
                return Fate.Static;
        }
    }
}
