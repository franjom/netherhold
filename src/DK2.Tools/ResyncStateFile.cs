// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DK2.Simulation;

namespace DK2.Tools;

/// <summary>Reads a <c>capture_export.py state</c> tick (<c>--state-only</c>) as a <see cref="ResyncState"/>.</summary>
public static class ResyncStateFile
{
    public static ResyncState Load(string path)
    {
        ResyncState? state;
        try { state = JsonSerializer.Deserialize<ResyncState>(File.ReadAllText(path), Json); }
        catch (JsonException ex) { throw new InvalidDataException($"{path} is not a complete capture_export.py state tick: {ex.Message}", ex); }
        if (state is null || state.Tiles.Length != 3 * state.Width * state.Height)
            throw new InvalidDataException($"{path} is not a capture_export.py state tick.");
        return state;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    if (info.Type != typeof(ResyncTerminal) && info.Type != typeof(ResyncHeart)) return;
                    foreach (JsonPropertyInfo property in info.Properties) property.IsRequired = true;
                },
            },
        },
    };
}
