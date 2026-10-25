// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Wall-clock front-end camera playback, preserving the last pose as native flag 0x8 does.</summary>
public sealed class FrontEndSession
{
    private readonly FrontEndFlow _flow;
    private readonly Func<FrontEndPath, IReadOnlyList<KcsFrame>?> _loadPath;
    private CameraPathPlayback? _path;
    private TimeSpan _now, _pathStarted;

    public FrontEndSession(Func<FrontEndPath, IReadOnlyList<KcsFrame>?> loadPath, FrontEndPage returnPage = FrontEndPage.None)
    {
        _loadPath = loadPath ?? throw new ArgumentNullException(nameof(loadPath));
        _flow = new FrontEndFlow(returnPage);
    }

    public FrontEndPage Page { get; private set; }
    public bool PathPlaying => _path is { Finished: false };
    public FrontEndPath? Path => _path is null ? null : (FrontEndPath)_path.PathId;
    public CameraPathPose? Pose => _path?.Pose();
    public FrontEndState State => _flow.State;
    public event Action<FrontEndOperation>? OperationRequested;
    public event Action<FrontEndPage>? PageChanged;

    public void Update(TimeSpan now)
    {
        if (now < _now) throw new ArgumentOutOfRangeException(nameof(now), "Front-end time cannot run backwards.");
        _now = now;
        _path?.Seek(1 + (now - _pathStarted).TotalSeconds * SimulationTiming.TicksPerSecond);
        Apply(_flow.Update(PathPlaying));
    }

    public bool TryTransition(FrontEndTransition transition)
    {
        if (PathPlaying) return false;
        IReadOnlyList<KcsFrame>? frames = _loadPath(transition.Path);
        if (frames is null || frames.Count == 0) return false;
        var nextPath = NewPath(transition.Path, frames);
        _flow.BeginTransition(transition);
        _path = nextPath;
        _pathStarted = _now;
        SetPage(FrontEndPage.None);
        return true;
    }

    private void Apply(FrontEndStep step)
    {
        if (step.StartPath is FrontEndPath path)
        {
            IReadOnlyList<KcsFrame>? frames = _loadPath(path);
            if (frames is null || frames.Count == 0) throw new InvalidOperationException($"Missing front-end camera path {(int)path}.");
            _path = NewPath(path, frames);
            _pathStarted = _now;
            SetPage(FrontEndPage.None);
        }
        if (step.ShowPage is FrontEndPage page) SetPage(page);
        if (step.Operation != FrontEndOperation.None) OperationRequested?.Invoke(step.Operation);
    }

    private void SetPage(FrontEndPage page)
    {
        if (Page == page) return;
        Page = page;
        PageChanged?.Invoke(page);
    }

    private static CameraPathPlayback NewPath(FrontEndPath path, IReadOnlyList<KcsFrame> frames)
        => new((int)path, frames, new TilePos(3, 12));
}
