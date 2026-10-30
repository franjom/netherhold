// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
namespace DK2.Presentation;
public enum ClassicOptionsAction : byte
{
    None, LevelObjective, GameOptions, Load, Save, EndGame, RestartLevel, ContinueGame, Back,
    ReturnToMenu, QuitGame, Confirm, Cancel,
}
public readonly record struct ClassicOptionsEntry(ClassicOptionsAction Action,uint TextId,float X,float Y,float Width);
public static class ClassicOptionsLayout
{
    public const uint TitleTextId = 94;
    public static IReadOnlyList<ClassicOptionsEntry> ForPage(ClassicOptionsPage page) => page switch
    {
        ClassicOptionsPage.Objective => ObjectiveEntries,
        ClassicOptionsPage.Exit => ExitEntries,
        ClassicOptionsPage.Confirmation => ConfirmationEntries,
        _ => Entries,
    };
    public static uint TitleFor(ClassicOptionsPage page, ClassicSessionAction confirmation) => page switch
    {
        ClassicOptionsPage.Exit => 1266,
        ClassicOptionsPage.Confirmation => confirmation switch
        {
            ClassicSessionAction.Restart => 1269,
            ClassicSessionAction.ReturnToMenu => 12,
            _ => 13,
        },
        _ => TitleTextId,
    };
    private static IReadOnlyList<ClassicOptionsEntry> ExitEntries { get; } = new ClassicOptionsEntry[]
    {
        new(ClassicOptionsAction.ReturnToMenu,12,8,136,1008),
        new(ClassicOptionsAction.QuitGame,13,8,200,1008),
        new(ClassicOptionsAction.ContinueGame,142,8,512,496),
        new(ClassicOptionsAction.Back,20,512,512,496),
    };
    private static IReadOnlyList<ClassicOptionsEntry> ConfirmationEntries { get; } = new ClassicOptionsEntry[]
    {
        new(ClassicOptionsAction.None,15,8,192,1008),
        new(ClassicOptionsAction.Confirm,21,8,352,1008),
        new(ClassicOptionsAction.ContinueGame,142,8,512,496),
        new(ClassicOptionsAction.Cancel,20,512,512,496),
    };
    public static IReadOnlyList<ClassicOptionsEntry> ObjectiveEntries { get; } = new ClassicOptionsEntry[]
    {
        new(ClassicOptionsAction.ContinueGame,142,8,512,496),
        new(ClassicOptionsAction.Back,20,512,512,496),
    };
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
