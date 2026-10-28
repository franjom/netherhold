// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The local startup clock's native lunation gate, independent of simulation ticks.</summary>
public static class CampaignMoon
{
    public static bool IsSecretFiveAvailable(float phase) => !(phase > -.95f && phase < .95f);

    public static float PhaseAt(DateTime localClock)
    {
        int day = 2415021 + (localClock.Date - new DateTime(1900, 1, 1)).Days;
        int seconds = localClock.Second + 60 * (localClock.Minute + 60 * localClock.Hour);
        if (localClock.Hour < 12) { day--; seconds += 43200; }
        else seconds -= 43200;
        float fraction = seconds * 1.1574074e-5f;
        var earlier = NewMoon(1);
        for (int cycle = 2; cycle <= 10001; cycle++)
        {
            var later = NewMoon(cycle);
            if ((earlier.Day < day || earlier.Day == day && earlier.Fraction <= fraction)
                && (day < later.Day || day == later.Day && fraction < later.Fraction))
            {
                float elapsed = day - earlier.Day + (fraction - earlier.Fraction);
                float length = later.Day - earlier.Day + (later.Fraction - earlier.Fraction);
                return 2 * (elapsed / length) - 1;
            }
            earlier = later;
        }
        return 0;
    }

    private static (int Day, float Fraction) NewMoon(int cycle)
    {
        double centuries = cycle * .0008085055;
        double sun = 359.2242 + cycle * 29.105356;
        double moon = 306.0253 + cycle * 385.81693 + centuries * centuries * .01073;
        double radians = .017453292500000002;
        float correction = (float)((.17339999973773956 - centuries * .000393) * Math.Sin(sun * radians)
            - .4068000018596649 * Math.Sin(moon * radians)
            + (float)((float)(centuries * centuries) * (.0001178 - centuries * 1.55e-7)
                + .75933 + cycle * 1.5305886));
        int wholeDays = (int)Math.Floor(correction);
        return (2415020 + 28 * cycle + wholeDays, correction - wholeDays);
    }
}
