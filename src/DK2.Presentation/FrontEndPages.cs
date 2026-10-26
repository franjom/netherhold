// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

public readonly record struct FrontEndWidgetId(ushort Value);
public readonly record struct FrontEndPoint(float X, float Y);
public readonly record struct FrontEndRect(float X, float Y, float Width, float Height)
{
    public bool Contains(FrontEndPoint point) => point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;
    public FrontEndRect Scale(float x, float y) => new(X * x, Y * y, Width * x, Height * y);
    public FrontEndRect Inflate(float padding) => new(X - padding, Y - padding, Width + 2 * padding, Height + 2 * padding);
    public FrontEndRect Intersect(FrontEndRect other)
    {
        float x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        return new(x, y, Math.Max(0, Math.Min(X + Width, other.X + other.Width) - x),
            Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - y));
    }
}

public enum FrontEndActionKind : uint { None, Station = 0x00538000, SwapPage = 0x005394e0, Dispatch = 0x005306f0 }
public enum FrontEndDrawKind : uint
{
    None, Label = 0x00532670, Title = 0x00536700, Logo = 0x005367d0,
    Accept = 0x00535f50, Cancel = 0x00536070, PlainText = 0x00532370,
    SmallText = 0x005322f0, MediumText = 0x00532310, KeyboardText = 0x00532350,
    OptionText = 0x00532330, DialogTitle = 0x00532390, BriefingSection = 0x0053A280,
    ResultsTable = 0x0053CAF0, HiScoreEntry = 0x0053D270, HiScoreRow = 0x0053DBD0,
}
public enum FrontEndTextAlignment : byte { Left, Right, Centre, Wrapped }
public readonly record struct FrontEndActionArguments(uint Pages, uint Commands)
{
    public FrontEndPage CurrentPage => (FrontEndPage)(Pages & 0xffff);
    public FrontEndPage NextPage => (FrontEndPage)(Pages >> 16);
    public ushort Command => (ushort)Commands;
    public ushort HighCommand => (ushort)(Commands >> 16);
}
public readonly record struct FrontEndAction(FrontEndActionKind Kind, FrontEndActionArguments Arguments);

/// <summary>Facts from a native 0x50-byte widget record; unknown callback addresses remain opaque data.</summary>
public sealed record FrontEndWidget(FrontEndWidgetId Id, byte NativeType, FrontEndRect DrawRect, FrontEndRect HitRect,
    ushort Anchor, FrontEndAction Primary, FrontEndAction Secondary, FrontEndPage OpenPage, bool CloseParent,
    uint TextId, FrontEndDrawKind Draw, uint DrawArgument1, uint DrawArgument2, ushort SoundSlot,
    uint NativeFrameCallback, uint NativeArt)
{
    public FrontEndTextAlignment Alignment => (FrontEndTextAlignment)(DrawArgument2 & 0xffff);
    public FrontEndRect ReferenceRect(FrontEndRect parent)
    {
        float x = (Anchor & 2) != 0 ? parent.X + parent.Width - DrawRect.X - DrawRect.Width : parent.X + DrawRect.X;
        float y = (Anchor & 4) != 0 ? parent.Y + parent.Height - DrawRect.Y - DrawRect.Height : parent.Y + DrawRect.Y;
        return new FrontEndRect(x, y, DrawRect.Width, DrawRect.Height).Scale(.25f, .25f);
    }
}
public sealed record FrontEndPageDefinition(FrontEndPage Id, FrontEndRect DrawRect, FrontEndRect HitRect,
    uint NativeFrameCallback, uint NativeDrawCallback, int Backdrop, IReadOnlyList<FrontEndWidget> Widgets);

public static partial class FrontEndPages
{
    public static IReadOnlyList<FrontEndPageDefinition> All { get; } = Array.AsReadOnly(CreateCatalogue());
    public static FrontEndPageDefinition Get(FrontEndPage id) => All.First(p => p.Id == id);
    public static FrontEndPageDefinition? Find(FrontEndPage id) => All.FirstOrDefault(p => p.Id == id);
}
