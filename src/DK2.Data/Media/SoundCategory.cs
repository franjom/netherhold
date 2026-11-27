// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Media;

public readonly record struct SoundCategory(string Name)
{
    public static SoundCategory None
    {
        get
        {
            return new(string.Empty);
        }
    }

    public bool IsNone
    {
        get
        {
            return string.IsNullOrEmpty(Name);
        }
    }

    public static SoundCategory FrontEnd
    {
        get
        {
            return new("front_end");
        }
    }

    public static SoundCategory FrontEndClick
    {
        get
        {
            return new("object_fe_parchment");
        }
    }

    public static SoundCategory Mentor
    {
        get
        {
            return new("speech_mentor");
        }
    }

    public static SoundCategory Music
    {
        get
        {
            return new("music");
        }
    }

    public static SoundCategory Ambience
    {
        get
        {
            return new("ambience");
        }
    }
}
public readonly record struct SpeechLineId(int Value);
public readonly record struct ResolvedSound(string Archive, SoundEntryId Entry);
public sealed record SoundMaps(BankMap Banks, SfxMap Effects);
