// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>How the Hand's hover target is drawn (gold-hover-and-sparkle.md §1-§2).
/// <para>Presentation_Update (<c>0x0043BE40</c>) tints the hover id the Hand published
/// (<see cref="HoverTint"/>). Before the tint, an installed art with flag
/// <see cref="HoverTextureArtFlag"/> has its texture phase forced to <see cref="HoverTexturePhase"/>
/// for the frame, which draws each material's last texture: the gold spill cards' <c>_HIGHLIGHT</c>
/// twin.</para></summary>
public static class ObjectHoverHighlight
{
    /// <summary>The art-resource flag Presentation_Update tests (<c>+0xB</c> &amp; 0x10000,
    /// <c>0x0043BE7D</c>). Set only on the gold meshes of objects 1, 2 and 3.</summary>
    public const uint HoverTextureArtFlag = 0x10000;

    /// <summary>The texture phase written to RenderInfo <c>+0x30</c> and <c>+0x34</c>
    /// (<c>0x0043BE9B</c>), restored after the world draw.</summary>
    public static readonly Fp32 HoverTexturePhase = new(0xFFF);

    /// <summary>Gold Pile 1, the Treasury and Heart store pile, which ours draws as a decoration.</summary>
    public const byte StorePileObjectId = 3;

    /// <summary>Whether a definition's mesh art carries <see cref="HoverTextureArtFlag"/>. DK2 tests
    /// the installed art; every mesh slot of the three gold objects carries it, so asking any
    /// mesh slot gives the same answer on the shipped data.</summary>
    public static bool UsesHoverTexture(ObjectDef? definition)
    {
        if (definition is null) return false;
        foreach (CreatureAnimationTiming art in definition.ArtSlots)
            if (art.ResourceType is 5 or 6 && ((uint)art.Flags & HoverTextureArtFlag) != 0) return true;
        return false;
    }

    /// <summary>The texture a material with <paramref name="textureCount"/> textures draws while
    /// hovered: the phase-to-texture rule (<see cref="DungeonHeartVisual.TextureIndex"/>) at
    /// <see cref="HoverTexturePhase"/>, the last one.</summary>
    public static int HoverTextureIndex(int textureCount) =>
        DungeonHeartVisual.TextureIndex(HoverTexturePhase, textureCount);

    /// <summary>Whether a drawn object may be the pick: any simulation object, or the store pile
    /// decoration that stands for DK2's Gold Pile 1 (SHOT0228 and SHOT0230 hover it). Other
    /// decorations stay out of the pick, as before.</summary>
    public static bool IsHoverTarget(int objectId, byte objectTypeId) =>
        objectId > 0 || (WorldView.IsDecoration(objectId) && objectTypeId == StorePileObjectId);
}

/// <summary>Which pick becomes the Hand's hover id, the thing Presentation_Update tints red
/// (Hand_UpdateHoverTarget <c>0x0040C290</c>; gold-hover-and-sparkle.md §2).</summary>
public static class HoverTint
{
    /// <summary>An object is published when its definition has CanBePickedUp or CanBeSlapped
    /// (<c>+0xF4</c> &amp; 0x300) and it is the viewer's, or it is loose gold (type 1 or 2) on the
    /// viewer's tile (<c>0x0049CBB0</c>). CONFIRMED.</summary>
    public static bool ObjectPublished(ObjectDef? definition, byte objectTypeId, byte owner, byte tileOwner, PlayerId viewer) =>
        definition is not null
        && (definition.Flags & (ObjectFlags.CanBePickedUp | ObjectFlags.CanBeSlapped)) != 0
        && (owner == viewer.Value || (objectTypeId is 1 or 2 && tileOwner == viewer.Value));

    /// <summary>The flash override's door/trap mode (<c>0x0043BEF8</c>..<c>0x0043BF1A</c>): a trap or door
    /// whose type (<c>+0xF6</c>) is 8 — the Fear Trap, the Hero Portcullis — gets mode 1, draw flags
    /// 0x600 rather than 0x200 (<c>0x0059D4B0</c>). CONFIRMED.</summary>
    public const byte FlashMode1Type = 8;

    /// <summary>Whether the hover tint's draw flags hold 0x400: the hit is a door or trap of type
    /// <see cref="FlashMode1Type"/>.</summary>
    public static bool CoversBlended(WorldState world, WorldHoverHit hit)
    {
        if (hit.Kind == WorldHoverKind.Door)
            foreach (DoorState door in world.Doors)
                if (door.Id == hit.EntityId) return door.DoorId == FlashMode1Type;
        if (hit.Kind == WorldHoverKind.Trap)
            foreach (TrapState trap in world.Traps)
                if (trap.Id == hit.EntityId) return trap.TrapId == FlashMode1Type;
        return false;
    }

    /// <summary>Whether the red pass draws over one material. Both mesh draws skip it for a
    /// material whose state is additive or alpha-blended (bit 0x1 or 0x20) unless the draw flags
    /// hold 0x400 (static 0x00581583..0x00581597, animated 0x00584FC9..0x00584FE3). CONFIRMED by the
    /// binary and by the owner (2026-10-23): the gold spill cards and a creature's translucent parts
    /// stay untinted.</summary>
    public static bool DrawsOverlay(bool blended, bool flag0x400) => !blended || flag0x400;

    /// <summary>Whether the hit is tinted for <paramref name="viewer"/>.
    /// <list type="bullet">
    /// <item>A creature the viewer may pick up (<c>0x0048E670</c>: the definition's hand bit and
    /// pickup permission; <see cref="HandRules.HasPickupPermission"/>). CONFIRMED.</item>
    /// <item>An object by <see cref="ObjectPublished"/>; the store pile decoration as Gold Pile 1
    /// owned by its store tile's keeper. CONFIRMED.</item>
    /// <item>A door or trap the viewer owns (owner, 2026-10-23). DK2 publishes any door and a trap on
    /// the viewer's or a neutral tile (<c>0x0040C290</c> cases 5 and 6); what the picker hands it is
    /// unread.</item>
    /// </list></summary>
    public static bool Applies(WorldState world, GameDatabase database, WorldHoverHit hit, PlayerId viewer)
    {
        switch (hit.Kind)
        {
            case WorldHoverKind.Creature:
                return world.CreatureById(hit.EntityId) is { } creature
                    && HandRules.HasPickupPermission(world, database, creature, viewer);
            case WorldHoverKind.Object:
                if (world.ObjectById(hit.EntityId) is { } thing)
                    return ObjectPublished(database.ObjectById(thing.ObjectTypeId), thing.ObjectTypeId,
                        thing.OwnerPlayerId, world.TileAtIndex(thing.TileIndex).OwnerPlayerId, viewer);
                if (!WorldView.IsStorePile(hit.EntityId)) return false;
                int tile = WorldView.DecorationTileIndex(hit.EntityId);
                if ((uint)tile >= (uint)(world.Width * world.Height)) return false;
                byte keeper = world.TileAtIndex(tile).OwnerPlayerId;
                return ObjectPublished(database.ObjectById(ObjectHoverHighlight.StorePileObjectId),
                    ObjectHoverHighlight.StorePileObjectId, keeper, keeper, viewer);
            case WorldHoverKind.Door:
                foreach (DoorState door in world.Doors)
                    if (door.Id == hit.EntityId) return door.PlayerId == viewer.Value;
                return false;
            case WorldHoverKind.Trap:
                foreach (TrapState trap in world.Traps)
                    if (trap.Id == hit.EntityId) return trap.PlayerId == viewer.Value;
                return false;
            default:
                return false;
        }
    }
}
