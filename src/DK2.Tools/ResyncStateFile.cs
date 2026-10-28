// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.Simulation;

namespace DK2.Tools;

/// <summary>Reads a <c>capture_state.py</c> tick (<c>--state-only</c>) as a <see cref="ResyncState"/>.</summary>
public static class ResyncStateFile
{
    public static ResyncState Load(string path)
    {
        ResyncState? state = JsonSerializer.Deserialize<ResyncState>(File.ReadAllText(path), Json);
        if (state is null || state.Tiles.Length != 3 * state.Width * state.Height)
            throw new InvalidDataException($"{path} is not a capture_state.py tick.");
        return state;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}
