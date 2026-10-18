// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
namespace DK2.Presentation;
public enum ClassicOptionsAction : byte { None, LevelObjective, GameOptions, Load, Save, EndGame, RestartLevel, ContinueGame }
public readonly record struct ClassicOptionsEntry(ClassicOptionsAction Action,uint TextId,float X,float Y,float Width);
public static class ClassicOptionsLayout
{
    public const uint TitleTextId = 94;
    public static IReadOnlyList<ClassicOptionsEntry> Entries { get; } = new ClassicOptionsEntry[]
    {
        new(ClassicOptionsAction.LevelObjective,537,8,136,496),
        new(ClassicOptionsAction.GameOptions,1265,8,200,496),
        new(ClassicOptionsAction.Load,143,8,264,496),
        new(ClassicOptionsAction.Save,201,8,328,496),
        new(ClassicOptionsAction.EndGame,1266,512,136,496),
        new(ClassicOptionsAction.RestartLevel,1269,512,200,1008),
        new(ClassicOptionsAction.ContinueGame,142,8,512,496),
    };
}
