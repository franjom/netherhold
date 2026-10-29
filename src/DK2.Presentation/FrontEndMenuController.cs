// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Data.Media;

namespace DK2.Presentation;

public readonly record struct FrontEndPointer(float X, float Y, bool PrimaryHeld);
public readonly record struct FrontEndSoundRequest(int Id);
public enum FrontEndDialogKind { Quit, ResetCampaign }
public sealed record FrontEndDialog(uint TitleTextId, uint BodyTextId, FrontEndPage ReturnPage,
    FrontEndDialogKind Kind = FrontEndDialogKind.Quit, uint SecondaryTextId = 0);
public readonly record struct FrontEndWidgetState(bool Enabled, bool Hovered, bool Pressed, int GlowFrame);

/// <summary>Front-end widget input and shell navigation. Later phases supply campaign and media actions.</summary>
public sealed class FrontEndMenuController : IDisposable
{
    private sealed class HoverState { public int Fade = -1; public bool SoundPlayed; }
    private readonly FrontEndSession _session;
    public CampaignMapController? Campaign { get; }
    public OptionsPagesController? Options { get; }
    private readonly Func<FrontEndWidget, FrontEndRect> _hitBounds;
    private readonly Dictionary<FrontEndWidgetId, HoverState> _hover = new();
    private FrontEndWidgetId? _underPointer;
    private bool _held;
    public FrontEndMenuController(FrontEndSession session, Func<FrontEndWidget, FrontEndRect> hitBounds, CampaignMapController? campaign = null, OptionsPagesController? options = null)
    {
        Campaign = campaign; Options = options;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _hitBounds = hitBounds ?? throw new ArgumentNullException(nameof(hitBounds));
        _session.PageChanged += PageChanged;
    }
    public FrontEndPage Page => _session.Page;
    public FrontEndDialog? Dialog { get; private set; }
    public IReadOnlyList<FrontEndWidget> Widgets => FrontEndPages.Find(Page)?.Widgets ?? Array.Empty<FrontEndWidget>();
    public event Action? QuitRequested;
    public event Action<FrontEndSoundRequest>? SoundRequested;
    public SoundRequests Sounds { get; } = new();

    private void RequestSound(int id, SoundCategory category)
    {
        Sounds.Add(new SoundRequest(category, new SoundId((uint)id)));
        SoundRequested?.Invoke(new(id));
    }
    public FrontEndMovieGallery? Movies { get; private set; }
    public event Action<string>? MovieRequested;
    public void SetMovieGallery(FrontEndMovieGallery gallery) => Movies = gallery;
    public event Action<string>? LoadRequested;
    public SaveSlotsView? Saves { get; private set; }
    public void SetSaveBrowser(SaveSlotsView view) => Saves = view;

    public bool IsEnabled(FrontEndWidget widget)
    {
        if (_session.PathPlaying) return false;
        ushort id = widget.Id.Value;
        return Page switch
        {
            FrontEndPage.Main => id is 1 or 3 or 4 or 5,
            FrontEndPage.SinglePlayer => id == 9 || id == 7 && Saves is not null || Campaign is not null && id is 6 or 567,
            FrontEndPage.Load => id == 74 || id == 73 && Saves?.Selected is not null && LoadRequested is not null,
            FrontEndPage.CampaignMap => Campaign is not null && id == 534,
            FrontEndPage.Briefing => Campaign is not null && (id == 169 || id == 170 && Campaign.CanLaunch),
            FrontEndPage.CampaignResults => Campaign is not null && id == 181,
            // Outside name entry page 18 is Today's Top Keepers, and its accept goes back to Extras
            // (DK2's static D0x41; after an entry DK2 may keep the entry's D0x68, Q657).
            FrontEndPage.HighScores => id == 47,
            FrontEndPage.Options => id is 110 or 111 or 112 or 113,
            FrontEndPage.Graphics => id is 129 or 130 || Options?.Supports(id) == true,
            FrontEndPage.Sound => id is 146 or 147 || Options?.Supports(id) == true,
            FrontEndPage.Controls => id is 159 or 160 || Options?.Supports(id) == true,
            FrontEndPage.Extras => id is 19 or 24 || id == 22 && Movies is not null || id == 21 && MovieRequested is not null,
            FrontEndPage.Movies => id == 65 || Movies is not null && id is >= 53 and <= 57
                && Movies.Entries[Movies.FirstRow + id - 53].Available && MovieRequested is not null,
            FrontEndPage.Dialog => Dialog is not null && id is 667 or 668,
            _ => false,
        };
    }

    public FrontEndWidgetState State(FrontEndWidget widget)
    {
        int fade = _hover.TryGetValue(widget.Id, out HoverState? state) ? state.Fade : -1;
        return new(IsEnabled(widget), _underPointer == widget.Id, _underPointer == widget.Id && _held,
            fade > 0 ? Math.Min(fade - 1, 3) : -1);
    }

    public void UpdatePointer(FrontEndPointer pointer)
    {
        _underPointer = null;
        var point = new FrontEndPoint(pointer.X, pointer.Y);
        foreach (FrontEndWidget widget in Widgets)
            if (IsEnabled(widget) && _hitBounds(widget).Contains(point))
            { _underPointer = widget.Id; break; }
        if (_underPointer is FrontEndWidgetId hovered)
        {
            FrontEndWidget widget = Widgets.First(w => w.Id == hovered);
            if (widget.Draw == FrontEndDrawKind.Label)
            {
                HoverState state = Hover(hovered);
                if (!state.SoundPlayed) { RequestSound(0x30f, SoundCategory.FrontEnd); state.SoundPlayed = true; }
                state.Fade = 8;
            }
        }
        bool pressed = pointer.PrimaryHeld && !_held;
        _held = pointer.PrimaryHeld;
        if (pressed && _underPointer is FrontEndWidgetId clicked) Activate(clicked);
    }

    public void AdvanceFrame()
    {
        foreach (HoverState state in _hover.Values)
            if (state.Fade >= 0 && --state.Fade < 0) state.SoundPlayed = false;
    }

    public bool Activate(FrontEndWidgetId id)
    {
        FrontEndWidget? widget = Widgets.FirstOrDefault(w => w.Id == id);
        if (widget is null || !IsEnabled(widget)) return false;
        int sound = widget.SoundSlot switch { 32 => 0x30a, 33 => 0x30b, 34 => 0x30c, 35 => 0x30d, 36 => 0x30e, _ => 0 };
        if (sound != 0) RequestSound(sound, SoundCategory.FrontEndClick);
        if (Page == FrontEndPage.Dialog)
        {
            if (id.Value == 667)
            {
                if (Dialog!.Kind == FrontEndDialogKind.ResetCampaign) return Campaign!.StartNew();
                QuitRequested?.Invoke();
            }
            else _session.TryShowPage(Dialog!.ReturnPage);
            return true;
        }
        if (Page == FrontEndPage.Movies)
        {
            if (id.Value == 65) return _session.TryShowPage(FrontEndPage.Extras);
            MovieRequested?.Invoke(Movies!.Entries[Movies.FirstRow + id.Value - 53].Path);
            return true;
        }
        if (Page == FrontEndPage.Extras && id.Value == 21)
        { MovieRequested?.Invoke("Data/Movies/Trailer.tgq"); return true; }
        if (Page == FrontEndPage.Load)
        {
            if (id.Value == 73) { LoadRequested?.Invoke(Saves!.Selected!.Path); return true; }
            return _session.TryTransition(new(FrontEndPath.MultiplayerToEntry, FrontEndState.ReturnSinglePlayer));
        }
        if (Campaign is not null)
        {
            if (Page == FrontEndPage.CampaignMap) return Campaign.BackToMenu();
            if (Page == FrontEndPage.Briefing) return id.Value == 169 ? Campaign.BackToMap() : Campaign.Launch();
            if (Page == FrontEndPage.CampaignResults) return Campaign.AcceptResults();
            // During name entry the page's accept finishes it (DK2 finishes from the edit field).
            if (Page == FrontEndPage.HighScores && Campaign.EntryRow >= 0) return Campaign.CommitEntry();
            if (Page == FrontEndPage.SinglePlayer && id.Value == 567) return Campaign.Continue();
            if (Page == FrontEndPage.SinglePlayer && id.Value == 6)
            {
                if (!CampaignProgression.NeedsResetConfirmation(Campaign.Profile)) return Campaign.StartNew();
                Dialog = new(140, 2847, FrontEndPage.SinglePlayer, FrontEndDialogKind.ResetCampaign, 15);
                return _session.TryShowPage(FrontEndPage.Dialog);
            }
        }
        if (Page is FrontEndPage.Graphics or FrontEndPage.Sound or FrontEndPage.Controls && Options is not null)
        {
            Options.Activate(widget);
            if (widget.Draw is not FrontEndDrawKind.Accept and not FrontEndDrawKind.Cancel) return true;
        }
        FrontEndAction action = widget.Primary;
        if (action.Kind == FrontEndActionKind.SwapPage)
            return _session.TryShowPage(action.Arguments.NextPage);
        if (action.Kind == FrontEndActionKind.Station)
        {
            FrontEndTransition? transition = action.Arguments.Command switch
            {
                5 => new(FrontEndPath.EntryToOptions, FrontEndState.OptionsReady),
                6 => new(FrontEndPath.EntryToExtras, FrontEndState.ExtrasReady),
                7 when Saves is not null => new(FrontEndPath.EntryToMultiplayer, FrontEndState.LoadReady),
                15 => new(FrontEndPath.OptionsToEntry, FrontEndState.ReturnMain),
                16 => new(FrontEndPath.ExtrasToEntry, FrontEndState.ReturnMain),
                _ => null,
            };
            if (transition.HasValue) return _session.TryTransition(transition.Value);
            if (action.Arguments.Command == 8)
            {
                Dialog = new(28, 15, FrontEndPage.Main);
                return _session.TryShowPage(FrontEndPage.Dialog);
            }
        }
        if (action.Kind == FrontEndActionKind.Dispatch)
        {
            // D0x41 points widget 47's show-page (+0x59) at page 6, which the press then shows (0x00528610).
            if (Page == FrontEndPage.HighScores && action.Arguments.Command == 0x41)
                return _session.TryShowPage(FrontEndPage.Extras);
            if (Page is FrontEndPage.Graphics or FrontEndPage.Sound or FrontEndPage.Controls)
                return _session.TryShowPage(FrontEndPage.Options);
            if (action.Arguments.NextPage != FrontEndPage.None)
                return _session.TryShowPage(action.Arguments.NextPage);
        }
        return false;
    }

    private HoverState Hover(FrontEndWidgetId id)
    {
        if (!_hover.TryGetValue(id, out HoverState? state)) _hover.Add(id, state = new());
        return state;
    }
    private void PageChanged(FrontEndPage page)
    {
        _underPointer = null; _hover.Clear();
        if (page is FrontEndPage.Graphics or FrontEndPage.Sound or FrontEndPage.Controls) Options?.Enter(page);
        if (page != FrontEndPage.Dialog) Dialog = null;
    }
    public void Dispose() => _session.PageChanged -= PageChanged;
}
