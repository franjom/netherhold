// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Unsigned renderer cache stamp, independent of simulation ticks; native comparisons do not correct wrap.</summary>
public readonly record struct ClassicLightFrame(uint Value);
