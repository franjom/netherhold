// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>One sprite of a flower: its texture under <see cref="StatusFlowerLayers.Folder"/>, its layer index (the
/// renderer lifts layer n by <c>n · <see cref="StatusFlowerLayers.CentreLift"/></c>, <see cref="StatusFlowerRules.LayerPlacement"/>),
/// the experience wedge's span or <see cref="StatusFlowerLayers.NoWedge"/>, and whether it is the petal ring.</summary>
public readonly record struct StatusFlowerLayer(string Name, int Index, float Wedge, bool Ring);

/// <summary>Which sprites a status flower draws, in draw order (status-flowers.md §2): the petal ring
/// <c>GUI\moods\H-0n</c> in the owner's colour, then the centre: the level badge at half colour with a full-colour
/// experience wedge, or a mood, job or status icon at full colour; then the Imprison tag when the viewer marked the
/// creature (0x005961A0) and, over a hero that trigger action 42 marked, the Objective ring (0x00596770). A dollar
/// flower draws none of them; the renderer draws the dollar mesh (<see cref="FlowerDollarClock"/>).</summary>
public static class StatusFlowerLayers
{
    public const string Folder = "GUI\\moods\\";

    /// <summary>The distance each later layer is drawn nearer the camera, along the eye ray, so sorting and depth put
    /// it on top while it stays concentric on screen.</summary>
    public const float CentreLift = 0.01f;

    public const float NoWedge = -1f;

    public const int MaxLayers = 4;

    private static readonly string[] Rings = ["H-00", "H-01", "H-02", "H-03", "H-04", "H-05"];

    /// <summary>Writes the flower's layers into <paramref name="layers"/> (at least <see cref="MaxLayers"/> long) and
    /// returns how many.</summary>
    public static int Build(in StatusFlower flower, Span<StatusFlowerLayer> layers)
    {
        if (flower.Dollar)
        {
            return 0;
        }

        int count = 0;
        if (flower.DrawRing)
        {
            layers[count++] = new StatusFlowerLayer(Rings[Math.Clamp(flower.Ring, 0, 5)], 0, NoWedge, Ring: true);
        }

        layers[count++] = new StatusFlowerLayer(flower.Centre, 1, flower.ExperienceArcs ? flower.Experience : NoWedge, Ring: false);
        if (flower.Imprison)
        {
            layers[count++] = new StatusFlowerLayer("Imprison", 2, NoWedge, Ring: false);
        }

        if (flower.Objective != null)
        {
            layers[count++] = new StatusFlowerLayer(flower.Objective, 3, NoWedge, Ring: false);
        }

        return count;
    }
}
