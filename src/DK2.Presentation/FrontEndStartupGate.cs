// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The title and intro movies run once per process: on the first front end entered fresh, not on one a level
/// returns to, and never after a skip has used the gate up.</summary>
public sealed class FrontEndStartupGate
{
    public bool Shown { get; private set; }

    /// <summary>Whether this front end runs the startup; any front end uses the gate up.</summary>
    public bool Begin(bool skip, FrontEndPage returnPage)
    {
        bool show = !skip && !Shown && returnPage == FrontEndPage.None;
        Shown = true;
        return show;
    }

    /// <summary>Reset the startup gate for a new run in the same process.</summary>
    public void Reset() => Shown = false;
}
