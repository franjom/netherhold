// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
namespace DK2.Presentation;

/// <summary>Three 256-entry display ramps; output is a word divided by 65535 (Q745).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/screen-colour-changes.md")]
public static class ScreenColourTables
{
    public const int ChannelSize = 256, Length = 3 * ChannelSize;
    public static ushort[] Build(ScreenColourKind kind, double gamma)
    {
        ushort[] table = new ushort[Length];
        Build(kind, gamma, table);
        return table;
    }
    /// <summary>An unknown kind retains the previous table, as the native jump table does.</summary>
    public static void Build(ScreenColourKind kind, double gamma, ushort[] table)
    {
        if ((byte)kind > 15)
        {
            return;
        }

        double exponent = 1 / Math.Clamp(gamma, 0.5, 2);
        const double third = (double)(1f / 3f), pi = (double)MathF.PI;
        for (int i = 0; i < ChannelSize; i++)
        {
            double g = Math.Pow(i / 256d, exponent), r = g, green = g, b = g;
            switch (kind)
            {
                case ScreenColourKind.Vampire: green = b = g * third; break;
                case ScreenColourKind.DarkElf: green = g * third; break;
                case ScreenColourKind.Skeleton: r = green = g / 2; b = Math.Min(1.5 * g, 1); break;
                case ScreenColourKind.Salamander:
                case ScreenColourKind.RedLift: r = Math.Min(2 * g, 1); break;
                case ScreenColourKind.DarkAngel: b = Math.Min(4 * g, 1); break;
                case ScreenColourKind.White: r = green = b = 1; break;
                case ScreenColourKind.Black: r = green = b = 0; break;
                case ScreenColourKind.Damage: r = 1; green = b = 0; break;
                case ScreenColourKind.Death: r = g / 2; green = b = 0; break;
                case ScreenColourKind.Blue: r = green = 0; b = Math.Min(1.5 * g, 1); break;
                case ScreenColourKind.Maiden: green = Math.Min(g + 0.2, 1); b = 0; break;
                case ScreenColourKind.ThreeBands:
                    double threeInput = (float)g;
                    r = green = b = 0;
                    if (g < 1d / 3)
                    {
                        b = (float)Math.Sin(pi * threeInput * 3);
                    }
                    else if (threeInput < 2d / 3)
                    {
                        green = Math.Sin((threeInput - 1d / 3) * (pi * 3));
                    }
                    else
                    {
                        r = green = b = (float)Math.Sin((threeInput - 2d / 3) * (pi * 3) * 0.5);
                    }

                    break;
                case ScreenColourKind.FourBands:
                case ScreenColourKind.FourBandsSwapped:
                    double fourInput = (float)g;
                    r = green = b = 0;
                    if (g < 0.25)
                    {
                        b = (float)Math.Sin(pi * fourInput * 4);
                    }
                    else if (fourInput < 0.5)
                    {
                        r = (float)Math.Sin((fourInput - 0.25) * (pi * 4));
                    }
                    else if (fourInput < 0.75)
                    {
                        green = Math.Sin((fourInput - 0.5) * (pi * 4));
                    }
                    else
                    {
                        r = green = b = (float)Math.Sin((fourInput - 0.75) * (pi * 4) * 0.5);
                    }

                    if (kind == ScreenColourKind.FourBandsSwapped)
                    {
                        (r, green) = (green, r);
                    }

                    break;
            }
            table[i] = Word(r);
            table[ChannelSize + i] = Word(green);
            table[2 * ChannelSize + i] = Word(b);
        }
    }
    private static ushort Word(double value) => unchecked((ushort)(int)(65535 * value));
}
