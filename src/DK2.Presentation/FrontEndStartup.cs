// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

public enum FrontEndStartupStage : byte { Title, BullfrogIntro, Intro, Done }
public enum FrontEndMovie : byte { BullfrogIntro, Intro }

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/front-end-menus.md#2-start-up-sequence")]
public sealed class FrontEndStartup
{
    private TimeSpan _now;
    public FrontEndStartupStage Stage { get; private set; }
    public FrontEndStartupStage Update(TimeSpan elapsed)
    {
        if (elapsed < _now) throw new ArgumentOutOfRangeException(nameof(elapsed));
        _now = elapsed;
        if (Stage == FrontEndStartupStage.Title && elapsed >= TimeSpan.FromSeconds(10))
            Stage = FrontEndStartupStage.BullfrogIntro;
        return Stage;
    }
    public void MovieFinished()
    {
        if (Stage == FrontEndStartupStage.BullfrogIntro) Stage = FrontEndStartupStage.Intro;
        else if (Stage == FrontEndStartupStage.Intro) Stage = FrontEndStartupStage.Done;
    }
    public void Skip() => MovieFinished();
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/front-end-menus.md#2-start-up-sequence")]
public sealed class FrontEndAttract
{
    private TimeSpan _now, _lastInput;
    private FrontEndMovie _next;
    public void Reset(TimeSpan now) => _lastInput = now;
    public FrontEndMovie? Update(TimeSpan now, FrontEndPage page, bool pointerMoved, bool keyPressed = false)
    {
        if (now < _now) throw new ArgumentOutOfRangeException(nameof(now));
        _now = now;
        if (pointerMoved || keyPressed) _lastInput = now;
        if (now - _lastInput <= TimeSpan.FromSeconds(180)) return null;
        _lastInput = now;
        if (page == FrontEndPage.None || (ushort)page is 10 or 11 or 32 or 35 or 39 or 44) return null;
        FrontEndMovie movie = _next;
        _next = movie == FrontEndMovie.BullfrogIntro ? FrontEndMovie.Intro : FrontEndMovie.BullfrogIntro;
        return movie;
    }
}
