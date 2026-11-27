// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>One frame's pointer, as <see cref="HoverCursorResolver.Resolve"/> needs it from the adapter.</summary>
/// <param name="Hit">The world hover, the adapter's pick (<see cref="HoverCursorResolver.FogHit"/> first); null when nothing was
/// picked: input not allowed, the pointer over the HUD or off the map, or no camera.</param>
/// <param name="Allowed">Ordinary input is on, the window focused and the pointer inside it.</param>
/// <param name="OverHud">The pointer is over the HUD (<see cref="HudPointerMap"/>), whether or not input is allowed.</param>
/// <param name="PrimaryHeld">The primary button is down, for the tag cursor's hold.</param>
/// <param name="Gold">The resource strip's gold, which prices the placement cursor.</param>
/// <param name="Milliseconds">The host's wall clock, in milliseconds.</param>
/// <param name="PointerWorld">Where the hand last pointed in the world, for the cursor's positional sounds.</param>
public readonly record struct HoverCursorInput(WorldHoverHit? Hit, bool Allowed, bool OverHud, bool PrimaryHeld, int Gold,
    long Milliseconds, FpPosition2? PointerWorld);

/// <summary>What the pointer shows this frame: the hand's cursor, the placement cursor (cage, tool icon, price), the creature the
/// status flowers stamp as hovered, the hovered creature's sound, and whether the hit takes the Hand's red tint (and the 0x400
/// blend). The adapter draws the overlay of <see cref="Placement"/> only while input is allowed.</summary>
public readonly record struct HoverFeedback(HandCursorKind Cursor, PlacementFeedback Placement, CreatureQueryTarget HoveredCreature,
    SoundRequest? HoverSound, bool Tinted, bool TintBlended)
{
    /// <summary>How far right of the pointer the hover tooltip clears the hand (<see cref="HandCursor.Reach"/>); 80 with no cursor.</summary>
    public float TooltipReach
    {
        get
        {
            return Cursor == HandCursorKind.Default ? 80 : HandCursor.Reach(Cursor);
        }
    }
}

/// <summary>Resolves cursor feedback after the hover pick: from the
/// pick, the tool state and the hand, the cursor, placement, tint, hover sound and hovered creature. It also holds the cast
/// hand (<see cref="SpellCastCursor"/>) and the spell refusal that gates it (<see cref="CastPosted"/>). Picking, the hover text,
/// drawing and playing sounds stay in the adapter. Presentation only: nothing here reaches the command stream.</summary>
public sealed class HoverCursorResolver
{
    private readonly HudToolState _tools;
    private readonly PlayerInputTranslator _input;
    private readonly ClassicHudControls _controls;
    private readonly BottomPanelView _panel;
    private readonly WorldView _view;
    private readonly HandCursorFeedback _cursor;
    private readonly PlayerId _viewer;
    private readonly SpellCastCursor _spellCast = new SpellCastCursor();

    /// <param name="cursor">The hand cursor's transient states, shared with <see cref="PickupFlow"/> and the host's presses.</param>
    public HoverCursorResolver(HudToolState tools, PlayerInputTranslator input, ClassicHudControls controls, BottomPanelView panel,
        WorldView view, HandCursorFeedback cursor, PlayerId viewer)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _viewer = viewer;
    }

    /// <summary>The last <see cref="Resolve"/>'s placement had No Go (before the held-icon gate), so a cast click plays no cast hand.</summary>
    public bool SpellRefused { get; private set; }

    /// <summary>The hover of an unexplored tile, under fog: the bare tile, which the adapter returns without picking; else null.</summary>
    public static WorldHoverHit? FogHit(WorldState world, PlayerId viewer, TilePos tile)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        return world.FogOfWarEnabled && !world.Tile(tile).IsExploredBy(viewer) ? new WorldHoverHit(WorldHoverKind.Tile, tile) : null;
    }

    /// <summary>Whether the objects-only key counts for the pick: no menu is modal and ordinary input is on. The pick takes
    /// objects only while this holds and the key is held with exact modifiers, which the adapter reads only then.</summary>
    public static bool ObjectsOnlyKeyCounts(HudVisibility visibility, bool modal)
    {
        return !modal && visibility.AllowOrdinaryInput;
    }

    /// <summary>The Hand's light follows the pick (0x0043B220, <see cref="ClassicPointLight.FromHand"/>).
    /// Over a thing it sits at the thing's position: the pick's output x and y are unread, and only
    /// that fits the owner's SHOT0126, where both of the Heart's stairs turn white. A door or trap
    /// takes the tile's centre. False for a tile or a vanished thing: the adapter then takes the
    /// pointer's hit on the tile's top plane (TileHeights), or the tile's centre when that misses.</summary>
    public static bool TryHandLightPoint(WorldState world, in WorldHoverHit hit, out Vector2 point)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (hit.Kind == WorldHoverKind.Creature && world.CreatureById(hit.EntityId) is CreatureState creature)
        {
            point = new Vector2(creature.PosX / (float)Fp32.One, creature.PosY / (float)Fp32.One);
        }
        else if (hit.Kind == WorldHoverKind.Object && world.ObjectById(hit.EntityId) is ObjectState thing)
        {
            point = new Vector2(thing.PosX / (float)Fp32.One, thing.PosY / (float)Fp32.One);
        }
        // A store pile stands for DK2's Gold Pile 1, a real thing there: its point, the tile centre
        // (WorldView.StorePiles places it with no offset).
        else if (hit.Kind == WorldHoverKind.Object && WorldView.IsStorePile(hit.EntityId))
        {
            int pile = WorldView.DecorationTileIndex(hit.EntityId);
            point = new Vector2(pile % world.Width + .5f, pile / world.Width + .5f);
        }
        else if (hit.Kind == WorldHoverKind.Door || hit.Kind == WorldHoverKind.Trap)
        {
            point = new Vector2(hit.Tile.X + .5f, hit.Tile.Y + .5f);
        }
        else { point = default; return false; }
        return true;
    }

    /// <summary>The Hand's light at <paramref name="point"/> (map x, y) for the hover <paramref name="hit"/>: raised over anything
    /// but a tile, black over an unexplored tile. The possession and follow arms (height 0x1200) and the player gates are not
    /// modelled.</summary>
    public static ClassicPointLight? HandLight(WorldState world, PlayerId viewer, in WorldHoverHit hit, Vector2 point,
        bool cheapLighting)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        bool explored = !world.FogOfWarEnabled || world.Tile(hit.Tile).IsExploredBy(viewer);
        return ClassicPointLight.FromHand(point.X, point.Y, hit.Kind != WorldHoverKind.Tile, explored, world.Width, world.Height,
            cheapLighting);
    }

    /// <summary>A world click posted <paramref name="command"/>: a cast plays the cast hand unless the hover refused it. DK2's
    /// click runs the cursor's own permission tests and posts only a permitted cast; ours posts it and only skips the hand.</summary>
    public void CastPosted(PlayerCommand command, long milliseconds)
    {
        if (!SpellRefused && HudToolState.CastSpellId(command) is byte cast)
        {
            _spellCast.Play(cast, milliseconds);
        }
    }

    /// <summary>This frame's feedback. Advances the hand's transients (<see cref="HandCursorFeedback.Resolve"/> or
    /// <see cref="HandCursorFeedback.Advance"/>), whose sounds the host then plays or clears, and the cast hand.</summary>
    public HoverFeedback Resolve(WorldState world, GameDatabase database, INavByteGrid navigation, in HoverCursorInput frame)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (database is null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        PlayerToolMode active = _tools.ActiveTool;
        HandCursorKind cursor = HandCursorKind.Point;
        PlacementFeedback placement = PlacementFeedback.None;
        CreatureQueryTarget hovered = CreatureQueryTarget.None;
        SoundRequest? sound = null;
        bool tinted = false, blended = false;
        if (frame.Allowed && !frame.OverHud && frame.Hit is WorldHoverHit hit)
        {
            TilePos tile = hit.Tile;
            // Hand_UpdateHoverTarget (0x0040C290) asks the hovered own creature's sound on each refresh of a qualifying turn.
            sound = CreatureHoverSound.For(world, database, hit, _viewer);
            bool pickup = false;
            // The flower's hover stamp reads the same hover id as the tint, in any tool (0x0043B7F1).
            if (hit.Kind == WorldHoverKind.Creature)
            {
                hovered = new CreatureQueryTarget(hit.EntityId);
            }

            if (hit.Kind == WorldHoverKind.Creature && world.CreatureById(hit.EntityId) is CreatureState creature)
            {
                pickup = HandRules.CanPickUp(world, database, creature, _viewer);
            }
            else if (hit.Kind == WorldHoverKind.Object && world.ObjectById(hit.EntityId) is ObjectState obj)
            {
                // A special is clicked to activate, never lifted (Hand_Click 0x00407AF1); the pointing hand over
                // it is the owner's observation, its producer unread (specials.md).
                pickup = database.ObjectById(obj.ObjectTypeId)?.IsSpecial != true
                    && HandRules.CanPickUpObject(world, database, obj, _viewer)
                    && HandRules.MayReachObject(world, database, obj, _viewer);
            }
            else if (hit.Kind == WorldHoverKind.Object && WorldView.IsStorePile(hit.EntityId))
            {
                // A Treasury or Heart pile, lifted whole from its own tile (gold-hover-and-sparkle.md §4).
                TilePos pileTile = world.TilePosition(WorldView.DecorationTileIndex(hit.EntityId));
                pickup = HandRules.MayReachStoreGold(world, pileTile, _viewer)
                    && HandRules.CanPickUpStoreGold(world, database, pileTile, _viewer);
            }
            bool dig = HandCursor.ShowsDig(world, database, tile, _viewer);
            if (active == PlayerToolMode.Hand)
            {
                cursor = HandCursor.ForHover(hit.Kind == WorldHoverKind.Tile, dig, pickup);
            }
            // Only the Hand's hover id is tinted: own pickable creatures, own pickable or
            // slappable objects and loose gold on own land, own doors and traps (0x0040C290).
            if (active == PlayerToolMode.Hand && HoverTint.Applies(world, database, hit, _viewer))
            {
                tinted = true;
                blended = HoverTint.CoversBlended(world, hit);
            }
            // In room mode a diggable tile keeps the pickaxe and draws no icon (0x0040B210).
            if (!(active == PlayerToolMode.BuildRoom && dig))
            {
                placement = _tools.Placement(world, database, navigation, _viewer, tile, hit, frame.Gold, _input, _panel);
            }
            // The arms test the dig tag on a tile hover target (+0x1084 == 1, +0x1088 bit 0).
            placement = HoverCaged(placement, world, database, tile, hit.Kind == WorldHoverKind.Tile && dig);
            placement = HeldIcons(placement, world, database, tile, hit.Kind == WorldHoverKind.Tile && dig);
        }
        if (frame.Allowed && frame.OverHud)
        {
            // Over the HUD DK2 shows the small pointing finger, state 2, which 0x0040BCB6 and
            // 0x0040BEFE set while interface +0x12B2 (pointer over the GUI) is set, in every tool
            // mode: 0x0040BBE0 requests it before its tool switch (HYPOTHESIS; its producer at
            // 0x00403D1C..0x00403D69 is partly read; owner-observed in play).
            cursor = HandCursorKind.SmallPoint;
            // Over the GUI 0x0040BBE0 sets No Go for the room, sell, door, trap and spell tools, and
            // 0x0040CAE0 for every tool that draws an icon, Query included (0x0040CDD9..0x0040CDE1).
            PlacementFeedback over = _tools.Placement(world, database, navigation, _viewer, null, default, frame.Gold, _input, _panel);
            if (over.Icon != PlacementIcon.None)
            {
                placement = new PlacementFeedback(null, false, over.Price, over.Icon, true, over.Affordable, over.SpellId);
            }
        }
        // A keeper spell's tool (mode 8) clears the contextual channel and requests state 12, the
        // pointing finger, every frame until the spell is cancelled (0x0040B7F0); over the GUI
        // 0x0040BBE0 asks SmallPoint instead and never runs the spell arm.
        bool spellArmed = _tools.SpellArmed(_input);
        if (!spellArmed)
        {
            _spellCast.Cancel();
        }
        else if (!frame.OverHud)
        {
            cursor = _spellCast.Showing(frame.Milliseconds) ? HandCursorKind.SpellCast : HandCursorKind.SpellPoint;
        }

        SpellRefused = placement.NoGo;
        bool holding = _view.LocalPickups.HasVisibleItems(world, _viewer);
        if (!holding && placement.Icon == PlacementIcon.Held)
        {
            placement = PlacementFeedback.None;
        }

        if (holding && placement.Icon == PlacementIcon.None && active == PlayerToolMode.Hand)
        {
            placement = new PlacementFeedback(placement.Cage, placement.Valid, placement.Price,
                PlacementIcon.Held, placement.NoGo, placement.Affordable);
        }

        if (active == PlayerToolMode.Hand && !_tools.QueryTool && !_controls.Modal && !spellArmed)
        {
            cursor = _cursor.Resolve(cursor, holding, _view.LocalPickups.HoldingGold(world, _viewer), frame.PrimaryHeld,
                frame.Milliseconds, frame.PointerWorld);
        }
        else
        {
            _cursor.Advance(frame.Milliseconds, frame.PointerWorld);
        }

        return new HoverFeedback(cursor, placement, hovered, sound, tinted, blended);
    }

    /// <summary>With no rectangle, the one-tile hover cage, most likely the Hand light's own (+0x33E,
    /// 0x0043B220; placement-cursor.md §2a), shown per tool as 0x004096B0 decides and hidden on a
    /// border tile, while Options is closed and nothing is possessed (0x0043B220 tests +0x76 bit
    /// 0x80000 and +0x19A). The full map is taken to be one of the view modes 1/2/10 that clear it
    /// (unread). Colour: <see cref="PlacementCursor.HoverCageBlue"/>. A full hand off a dig tile shows
    /// the drop cage instead (0x0040AE80).</summary>
    private PlacementFeedback HoverCaged(in PlacementFeedback feedback, WorldState world, GameDatabase database, TilePos at, bool dig)
    {
        if (feedback.Cage is not null || _controls.Modal || world.Player(_viewer).PossessedCreatureId != 0
            || _tools.CageTool is not HoverCageTool tool)
        {
            return feedback;
        }

        bool holds = world.Player(_viewer).Hand.Count != 0;
        if (!PlacementCursor.HoverCageShows(tool, dig, holds))
        {
            return feedback;
        }

        if (tool == HoverCageTool.Hand && !dig)
        {
            return PlacementCursor.HandDrop(world, database, _viewer, at);
        }

        return new PlacementFeedback(PlacementCursor.HoverCage(at, world.Width, world.Height),
            PlacementCursor.HoverCageBlue(tool, dig, feedback.Valid), feedback.Price, feedback.Icon, feedback.NoGo, feedback.Affordable,
            feedback.SpellId);
    }

    /// <summary>The Hand's held-item icons (0x0040CAE0 mode 0, 0x0040CFE0), gated apart from the cage:
    /// 0x0040CAE0 returns at once while Options is open (+0x2722; owner-confirmed 2026-10-23) or bit
    /// 0x01 is set (0x004039A0 sets it once the player has a result, Status +0x19A), and draws the
    /// icons only with bit 0x08 (not over a dig-taggable tile with nothing to pick up) off the GUI.
    /// Its caller 0x00403FB0 calls it every frame; neither tests possession or the full map, so
    /// neither hides the icons here.</summary>
    private PlacementFeedback HeldIcons(in PlacementFeedback feedback, WorldState world, GameDatabase database, TilePos at, bool dig)
    {
        if (feedback.Icon == PlacementIcon.Held || dig || _controls.OptionsVisible || _tools.ActiveTool != PlayerToolMode.Hand
            || _tools.QueryTool || _tools.PanelChoice is not null || world.Player(_viewer).Hand.Count == 0)
        {
            return feedback;
        }

        PlacementFeedback held = PlacementCursor.HandDrop(world, database, _viewer, at);
        return new PlacementFeedback(feedback.Cage, feedback.Valid, 0, PlacementIcon.Held, held.NoGo, true, 0, held.Dispose);
    }
}
