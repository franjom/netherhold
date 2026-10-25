// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public sealed record FrontEndProp(ObjectView View, bool Visible = true,
    FrontEndMapPiece? MapPiece = null, PetDungeonMapPiece? PetDungeonPiece = null, CampaignSlot? MarkerSlot = null);

/// <summary>Decorative front-end objects from 0x004DEF30; never inserts simulation entities.</summary>
public static class FrontEndScenery
{
    public static IReadOnlyList<FrontEndProp> Create(WorldState world, GameDatabase database,
        CampaignProfile profile, SimRandom visualRandom, bool secretFiveVisible = false)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (visualRandom is null) throw new ArgumentNullException(nameof(visualRandom));
        var props = new List<FrontEndProp>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.HeroGateFrontEnd) continue;
            int[] slabs = SlabChain.Order(world, room);
            for (int index = 0; index < slabs.Length; index++)
            {
                var tile = new TilePos(slabs[index] % world.Width, slabs[index] / world.Width);
                PlayerId owner = new(room.OwnerPlayerId);
                if (index is 0 or 3 or 6 or 9 or 5 or 8 or 11 or 14)
                    AddObject(132, index is 0 or 3 or 6 or 9 ? 0x400 : 0, animated: true);
                if (index is 1 or 7) { AddObject(89, 0x400, true); AddObject(134, 0x400, true); }
                if (index is 4 or 10) { AddObject(135, 0x400, true); AddObject(136, 0x400, true); }
                if (index == 1) AddObject(131, 0x400, false);
                if (index == 3)
                    foreach (PetDungeonMapPiece piece in Enum.GetValues(typeof(PetDungeonMapPiece)))
                        props.Add(new FrontEndProp(View(133, FrontEndAssets.PetDungeonPiece(piece), 0x600, 0x19A,
                                retainLight: piece == PetDungeonMapPiece.First),
                            PetDungeonPiece: piece));
                if (index == 10)
                    foreach (FrontEndMapPiece piece in Enum.GetValues(typeof(FrontEndMapPiece)))
                    {
                        int secret = (int)piece - (int)FrontEndMapPiece.Secret1;
                        bool visible = secret < 0 || (piece == FrontEndMapPiece.Secret5 ? secretFiveVisible : profile.SecretLevels[secret] != 0);
                        props.Add(new FrontEndProp(View(102, FrontEndAssets.CampaignPiece(piece), 0x400,
                            retainLight: piece == FrontEndMapPiece.Level1), visible, piece));
                    }
                void AddObject(byte type, int heading, bool animated)
                {
                    ObjectDef definition = database.ObjectById(type)
                        ?? throw new InvalidOperationException($"Front-end object {type} is missing.");
                    Fp32 phase = animated ? new Fp32(visualRandom.NextInt(Fp32.One)) : default;
                    props.Add(new FrontEndProp(View(type, AssetRef.ForObject(definition.Name, AssetKind.Mesh), heading,
                        animated: animated, phase: phase)));
                }

                ObjectView View(byte type, AssetRef key, int heading, int offsetX = 0x800, bool animated = false,
                    Fp32 phase = default, bool retainLight = true)
                    => new(-1_000_000 - props.Count, type, owner, tile, key, 0,
                        tile.X * Fp32.One + offsetX, tile.Y * Fp32.One + 0x800, Fp32.One, heading, Fp32.One,
                        playsAnimation: animated, light: retainLight && database.ObjectById(type)?.Light is { Radius: > 0 } light ? light : null,
                        animationInitialPhase: phase);
            }
        }
        return props.AsReadOnly();
    }
    public static IReadOnlyList<FrontEndProp> CreateCampaignMarkers(GameDatabase database)
    {
        var markers = new List<FrontEndProp>();
        for (int value = 1; value <= 24; value++)
        {
            var slot = new CampaignSlot(value);
            var view = new ObjectView(-2_000_000 - value, 115, new PlayerId(PlayerIds.Neutral), new TilePos(3, 10),
                FrontEndAssets.CampaignMarker(slot), 0, 0x3800, 0xa800, Fp32.One, 0x400, Fp32.One);
            markers.Add(new FrontEndProp(view, false, MarkerSlot: slot));
        }
        return markers.AsReadOnly();
    }

    public static IReadOnlyList<FrontEndProp> ApplyCampaignMap(IReadOnlyList<FrontEndProp> original, CampaignMapController map)
    {
        return Array.AsReadOnly(original.Select(prop =>
        {
            CampaignMapVisual visual;
            if (prop.MapPiece is FrontEndMapPiece piece) visual = map.LandVisual(new CampaignSlot((byte)piece));
            else if (prop.MarkerSlot is CampaignSlot slot) visual = map.MarkerVisual(slot);
            else return prop;
            ObjectView v = prop.View;
            var view = new ObjectView(v.Id, v.ObjectTypeId, new PlayerId(v.OwnerPlayerId), v.Tile, v.Mesh, v.GoldHeld,
                v.PosX, v.PosY, v.Height + visual.Lift, v.Heading, v.Scale, v.Pitch, v.Roll,
                playsAnimation: v.PlaysAnimation, light: v.Light, animationInitialPhase: v.AnimationInitialPhase,
                texturePhase: visual.TexturePhase);
            return prop with { View = view, Visible = visual.Visible };
        }).ToArray());
    }

}
