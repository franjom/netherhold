// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>Headings in the original's unit, 2048 to a turn, and the one-tick turn toward a
/// target (<c>0x004B2AE0</c>; docs/research/steering-behaviours.md §5.3). The bearing is the
/// original's table arctangent (<c>0x0065FB54</c>), whose 257 entries are exactly
/// floor(atan(i/256) · 2048 / 2π); the table here is that formula's output, not a copy.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class Bearing
{
    public const int Turn = 2048;
    public const int HalfTurn = Turn / 2;
    public const int Mask = Turn - 1;

    private static readonly int[] Arctangent =
    [
          0,   1,   2,   3,   5,   6,   7,   8,  10,  11,  12,  13,  15,  16,  17,  19,
         20,  21,  22,  24,  25,  26,  27,  29,  30,  31,  32,  34,  35,  36,  38,  39,
         40,  41,  43,  44,  45,  46,  48,  49,  50,  51,  53,  54,  55,  56,  57,  59,
         60,  61,  62,  64,  65,  66,  67,  68,  70,  71,  72,  73,  75,  76,  77,  78,
         79,  81,  82,  83,  84,  85,  87,  88,  89,  90,  91,  92,  94,  95,  96,  97,
         98,  99, 101, 102, 103, 104, 105, 106, 107, 109, 110, 111, 112, 113, 114, 115,
        116, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 129, 130, 131, 132, 133,
        134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 147, 148, 149, 150,
        151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166,
        167, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181,
        182, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 191, 192, 193, 194, 195,
        196, 197, 198, 198, 199, 200, 201, 202, 203, 203, 204, 205, 206, 207, 208, 208,
        209, 210, 211, 212, 212, 213, 214, 215, 216, 216, 217, 218, 219, 220, 220, 221,
        222, 223, 223, 224, 225, 226, 226, 227, 228, 229, 229, 230, 231, 232, 232, 233,
        234, 235, 235, 236, 237, 237, 238, 239, 239, 240, 241, 242, 242, 243, 244, 244,
        245, 246, 246, 247, 248, 248, 249, 250, 250, 251, 252, 252, 253, 254, 254, 255,
        256,
    ];

    /// <summary>The original's <c>atan(i/256)</c> table, exposed so a test can hold it to its
    /// formula.</summary>
    public static int ArctangentEntry(int i) => Arctangent[i];

    /// <summary>The bearing of a difference vector by octant: +x is 0x200, +y 0x400, -x 0x600 and
    /// -y 0, with the origin itself 0. Any single unit will do; the mover passes 1/4096 tiles.
    /// </summary>
    public static int Of(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return 0;
        if (dx >= 0)
        {
            if (dy >= 0) return dx >= dy ? Wrap(Arc(dy, dx) + 0x200) : Wrap(0x400 - Arc(dx, dy));
            int ady = -dy;
            return dx >= ady ? Wrap(0x200 - Arc(ady, dx)) : Wrap(Arc(dx, ady));
        }

        int adx = -dx;
        if (dy >= 0) return adx >= dy ? Wrap(0x600 - Arc(dy, adx)) : Wrap(0x400 + Arc(adx, dy));
        int ay = -dy;
        return adx >= ay ? Wrap(0x600 + Arc(ay, adx)) : Wrap(0x800 - Arc(adx, ay));
    }

    /// <summary>The shorter way round from a heading to a target, 0..1024 (<c>0x005119F0</c>).</summary>
    public static int Distance(int heading, int target)
    {
        int d = Fixed16.Abs(heading - target);
        if (d > HalfTurn) d = -d;
        return d & Mask;
    }

    /// <summary>Whether the heading increases to reach the target (<c>0x00511A30</c>). Equal
    /// headings and a target exactly half a turn ahead both answer yes.</summary>
    public static bool TurnsUp(int heading, int target)
    {
        int d = target - heading;
        if (d == 0) return true;
        return Fixed16.Abs(d) > HalfTurn ? d < 0 : d >= 0;
    }

    /// <summary>One tick of the turn toward a target (<c>0x004B2AE0</c>): step the heading by at
    /// most <paramref name="rate"/> and return the distance still to turn. A zero rate never
    /// arrives.</summary>
    public static int TurnToward(ref int heading, int target, int rate)
    {
        int distance = Distance(heading, target);
        if (distance == 0) return 0;
        int step = rate <= 0 ? 0 : (rate < distance ? rate : distance);
        heading = Wrap(TurnsUp(heading, target) ? heading + step : heading - step);
        return Distance(heading, target);
    }

    private static int Arc(int smaller, int larger) => Arctangent[(int)(((uint)smaller << 8) / (uint)larger)];

    private static int Wrap(int angle) => angle & Mask;
}
