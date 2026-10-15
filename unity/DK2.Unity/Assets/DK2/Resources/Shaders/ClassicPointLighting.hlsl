// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
#ifndef DK2_CLASSIC_POINT_LIGHTING_INCLUDED
#define DK2_CLASSIC_POINT_LIGHTING_INCLUDED

bool DK2PointContribution(float3 vertex, float3 normal, float4 positionRadiusSquared,
    float4 colourIntensity, out float3 contribution)
{
    contribution = 0;
    float3 toLight = positionRadiusSquared.xyz - vertex;
    float distanceSquared = dot(toLight, toLight);
    float radiusSquared = positionRadiusSquared.w;
    if (radiusSquared <= 0 || distanceSquared >= radiusSquared) return true;
    float incidence = dot(normal, toLight) * colourIntensity.w;
    if ((asuint(incidence) & 0x80000000u) != 0) return true;
    int index = (int)round(distanceSquared * 16.0 - 0.49999);
    if (index < 0 || index >= 256) return false;
    if (index == 0)
    {
        // Positive incidence is proved; zero's x87 exception-mask dependence is not.
        if (incidence == 0) return false;
        contribution = colourIntensity.rgb;
        return true;
    }
    float lookup = 0.25 + 16.0 / index;
    float factor = incidence * lookup * (radiusSquared - distanceSquared) / radiusSquared;
    contribution = colourIntensity.rgb * min(factor, 1.0);
    return true;
}

float3 DK2PackedDiffuse(float3 normalizedLightSum)
{
    return min(floor(max(normalizedLightSum, 0.0) * 255.0 + 0.00001), 255.0) / 255.0;
}
#endif
