// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>The <see cref="SoundCategory"/> of a category name as the data spells it, made once per name: the maps' files
/// are named in lower case and the data's names are not, and a request made every frame or every step must not allocate
/// the lower-case copy each time. Thread-safe.</summary>
public sealed class SoundCategoryNames
{
    private readonly Dictionary<string, SoundCategory> _lower = new Dictionary<string, SoundCategory>(StringComparer.Ordinal);

    /// <summary>The category, <see cref="SoundCategory.None"/> for a null or empty name.</summary>
    public SoundCategory Of(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return SoundCategory.None;
        }

        lock (_lower)
        {
            if (!_lower.TryGetValue(name!, out SoundCategory category))
            {
                _lower.Add(name!, category = new SoundCategory(name!.ToLowerInvariant()));
            }

            return category;
        }
    }
}
