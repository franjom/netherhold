// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public readonly record struct CampaignMapVisual(bool Visible, Fp32 TexturePhase, int Lift);

/// <summary>Campaign station selection and native texture/height presentation over an immutable profile.</summary>
public sealed class CampaignMapController : IDisposable
{
    private readonly FrontEndSession _session;
    private readonly Func<CampaignSlot, CampaignBriefing> _loadBriefing;
    private readonly int[] _lift = new int[30];
    private readonly int[] _landPhases = new int[30], _markerPhases = new int[24];
    private bool _introPending;
    private CampaignSlot _selected;
    private TimeSpan _entered;
    private bool _held;
    private int _blink;
    public CampaignMapController(FrontEndSession session, CampaignProfile profile, bool secretFiveAvailable,
        Func<CampaignSlot, CampaignBriefing> loadBriefing, IReadOnlyList<HiScoreEntry>? hiScores = null)
    {
        HiScores = hiScores ?? HiScoreTable.Empty;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _loadBriefing = loadBriefing ?? throw new ArgumentNullException(nameof(loadBriefing));
        Profile = CampaignProgression.EnsureOpenMap(profile ?? throw new ArgumentNullException(nameof(profile)));
        SecretFiveAvailable = secretFiveAvailable;
        _session.OperationRequested += OnOperation;
        _session.Updated += AdvanceVisuals;
        ApplyLandPhases(); // 0x005395A0 also runs once the front-end level has loaded.
    }
    public CampaignProfile Profile { get; private set; }
    public bool SecretFiveAvailable { get; }
    public CampaignBriefing? Briefing { get; private set; }
    /// <summary>The statistics page's rows, null outside it or with no recorded block.</summary>
    public CampaignResults? Results { get; private set; }
    public IReadOnlyList<HiScoreEntry> HiScores { get; private set; }
    /// <summary>The hi-score row whose name is being typed, or -1.</summary>
    public int EntryRow { get; private set; } = -1;
    public event Action<IReadOnlyList<HiScoreEntry>>? HiScoresChanged;
    public CampaignSlot Hovered { get; private set; }
    public int Revision { get; private set; }
    public bool IsAtMap => _session.Page == FrontEndPage.CampaignMap && !_session.PathPlaying;
    public event Action<CampaignProfile>? ProfileChanged;
    public event Action<CampaignSlot>? LaunchRequested;

    public bool Continue() => BeginMap(Profile);
    public bool StartNew() => BeginMap(CampaignProgression.NewCampaign(Profile));
    private bool BeginMap(CampaignProfile profile)
    {
        if (_session.PathPlaying || !_session.TryTransition(new(FrontEndPath.EntryToTable, FrontEndState.CampaignMap))) return false;
        if (!ReferenceEquals(profile, Profile)) ChangeProfile(profile);
        EnterMap(); return true;
    }
    public void Hover(CampaignSlot slot)
    {
        var next = IsAtMap && CampaignProgression.CanSelect(Profile, slot, SecretFiveAvailable) ? slot : CampaignSlot.None;
        if (Hovered == next) return;
        if (Hovered.IsCampaign) _markerPhases[Hovered.Value - 1] = 0;
        if (next.IsCampaign) _markerPhases[next.Value - 1] = 0xfff;
        Hovered = next; Revision++;
    }
    public void UpdatePointer(CampaignSlot slot, bool primaryHeld)
    {
        Hover(slot);
        bool pressed = primaryHeld && !_held;
        _held = primaryHeld;
        if (pressed && Hovered != CampaignSlot.None) Select(Hovered);
    }
    public bool Select(CampaignSlot slot)
    {
        if (!IsAtMap || !CampaignProgression.CanSelect(Profile, slot, SecretFiveAvailable)) return false;
        if (!_session.TryTransition(new(FrontEndPath.TableToBriefing, FrontEndState.PrepareCampaign))) return false;
        _selected = slot; Briefing = null; Hovered = CampaignSlot.None;
        ChangeProfile(Profile with { SecretLevelNumber = slot.IsSecret ? (uint)(slot.Value - 25) : 0 });
        Revision++; return true;
    }
    public bool BackToMap()
    {
        if (_session.Page != FrontEndPage.Briefing) return false;
        Briefing = null; _selected = CampaignSlot.None;
        return _session.TryTransition(new(FrontEndPath.BriefingToTable, FrontEndState.CampaignMap));
    }
    public bool BackToMenu()
    {
        if (!IsAtMap) return false;
        bool changed = _session.TryTransition(new(FrontEndPath.TableToEntry, FrontEndState.SinglePlayerReady));
        if (changed) { Hovered = CampaignSlot.None; Revision++; }
        return changed;
    }
    public bool Launch()
    {
        if (_session.Page != FrontEndPage.Briefing || Briefing is null || LaunchRequested is null) return false;
        LaunchRequested.Invoke(Briefing.Slot); return true;
    }
    public bool CanLaunch => LaunchRequested is not null && Briefing is not null;

    /// <summary>The statistics page's accept, dispatcher 0x55: Level Number moves on after a win; a
    /// won map whose Total Evil Rating qualifies opens name entry on page 18 (0x0053E320), else
    /// path 254 returns to the map (state 0x12).</summary>
    public bool AcceptResults()
    {
        if (_session.Page != FrontEndPage.CampaignResults || _session.PathPlaying) return false;
        CampaignSlot played = _selected;
        CampaignProfile advanced = CampaignProgression.AdvanceAfterResults(Profile);
        if (!ReferenceEquals(advanced, Profile)) ChangeProfile(advanced);
        uint rating = played == CampaignSlot.None ? 0 : Profile.TotalEvilRating[played.Value - 1];
        bool won = LevelStatisticsBlock.FromDwords(Profile.PlayerStatistics, PlayerIds.FirstKeeper).LevelWon;
        if (played != CampaignSlot.None && won && HiScoreTable.Qualifies(HiScores, rating))
        {
            EntryRow = HiScoreTable.Position(HiScores, rating);
            HiScores = HiScoreTable.Insert(HiScores, EntryRow, new HiScoreEntry(rating, "",
                HiScoreTable.Label(played, Briefing?.Tagline ?? "")));
            Revision++;
            return _session.TryShowPage(FrontEndPage.HighScores);
        }
        return FlyBackToMap();
    }

    public bool TypeEntry(char character)
    {
        if (EntryRow < 0 || char.IsControl(character) || HiScores[EntryRow].Name.Length >= HiScoreTable.MaxText) return false;
        SetEntryName(HiScores[EntryRow].Name + character); return true;
    }

    public bool EraseEntry()
    {
        string name = EntryRow < 0 ? "" : HiScores[EntryRow].Name;
        if (name.Length == 0) return false;
        SetEntryName(name.Substring(0, name.Length - 1)); return true;
    }

    /// <summary>The entry's finish (dispatcher 0x00531BE5): the row keeps the typed name, the table is
    /// saved (0x0053E610) and path 254 returns to the map.</summary>
    public bool CommitEntry()
    {
        if (EntryRow < 0 || _session.PathPlaying) return false;
        EntryRow = -1;
        HiScoresChanged?.Invoke(HiScores);
        return FlyBackToMap();
    }

    private void SetEntryName(string name)
    {
        var rows = HiScores.ToArray();
        rows[EntryRow] = rows[EntryRow] with { Name = name };
        HiScores = Array.AsReadOnly(rows); Revision++;
    }

    private bool FlyBackToMap()
    {
        if (!_session.TryTransition(new(FrontEndPath.BriefingToTable, FrontEndState.CampaignMapEnter))) return false;
        Briefing = null; Results = null; _selected = CampaignSlot.None;
        Revision++; return true;
    }

    public CampaignMapVisual LandVisual(CampaignSlot slot)
    {
        bool visible = slot.IsCampaign || slot.Value == 25 || slot.IsSecret && CampaignProgression.CanSelect(Profile, slot, SecretFiveAvailable);
        int phase = IsMapStation && slot.Value != 0 ? _landPhases[slot.Value - 1] : 0;
        return new(visible, new Fp32(phase), slot.Value == 0 ? 0 : _lift[slot.Value - 1]);
    }
    public CampaignMapVisual MarkerVisual(CampaignSlot slot)
    {
        bool visible = IsAtMap && slot.IsCampaign && Profile.PlayerLevelStatus[slot.Value - 1] == CampaignLevelStatus.Open;
        int phase = slot.IsCampaign ? _markerPhases[slot.Value - 1] : 0;
        return new(visible, new Fp32(phase), 0);
    }
    private bool IsMapStation => IsAtMap || _session.Page is FrontEndPage.Briefing or FrontEndPage.CampaignResults
        || _session.Path is FrontEndPath.EntryToTable or FrontEndPath.TableToBriefing or FrontEndPath.BriefingToTable;
    private void EnterMap()
    {
        _entered = _session.Now; _blink = 0xfff; _introPending = true; Hovered = CampaignSlot.None;
        Array.Clear(_markerPhases, 0, _markerPhases.Length);
        ApplyLandPhases();
        Revision++;
    }
    private void ApplyLandPhases()
    {
        for (int i = 0; i < 30; i++) _landPhases[i] = i < 25 && Profile.PlayerLevelStatus[i]
            is CampaignLevelStatus.Won or CampaignLevelStatus.ClosedBranch ? 0x55a : 0;
    }
    private void OnOperation(FrontEndOperation operation)
    {
        if (operation == FrontEndOperation.EnterCampaignMap) EnterMap();
        if (operation is FrontEndOperation.CampaignMapFrame or FrontEndOperation.EnterCampaignMap)
        {
            _session.TryShowPage(FrontEndPage.CampaignMap);
        }
        if (operation == FrontEndOperation.ResultsFourth)
        {
            // 0x0053C3C0 case 4 reloads the played map's briefing (0x0053AC60) for the statistics page.
            _selected = CampaignProgression.PlayedSlot(Profile);
            Briefing = _selected == CampaignSlot.None ? null : _loadBriefing(_selected);
            Results = CampaignResults.Build(Profile, _selected);
            Revision++;
        }
        if (operation == FrontEndOperation.PrepareCampaign && Briefing is null && _selected != CampaignSlot.None)
        {
            Briefing = _loadBriefing(_selected);
            ChangeProfile(Profile with { LevelNumber = _selected.Value });
            _session.TryShowPage(FrontEndPage.Briefing); Revision++;
        }
    }
    private void AdvanceVisuals(TimeSpan now)
    {
        if (IsAtMap)
        {
        long elapsed = (long)(_session.Now - _entered).TotalMilliseconds;
        var routes = RouteSlots(Profile.LevelNumber);
        if (elapsed < 10000)
        {
            if (routes.Count == 0) _entered = _session.Now - TimeSpan.FromSeconds(10);
            else foreach (int slot in routes) SetPhase(_landPhases, slot - 1, _blink);
        }
        else
        {
            if (_introPending)
            {
                if (_blink == 0) foreach (int slot in routes) SetPhase(_landPhases, slot - 1, 0);
                else _blink = 0;
                _introPending = false;
            }
            if (Hovered == CampaignSlot.None)
                for (int i = 0; i < 24; i++)
                    if (Profile.PlayerLevelStatus[i] == CampaignLevelStatus.Open) SetPhase(_markerPhases, i, _blink);
        }
        _blink = ((long)(_session.Now - _entered).TotalMilliseconds / 200 & 1) != 0 ? 0xfff : 0;
        }
        // 0x00538D00 raises the hovered land 8 units a frame; 0x004A35E0 lowers the others 8 units an object
        // tick, 2.25 s from the top if the front end ticks at the game's four a second (its rate is unread).
        // Owner's choice (2026-10-26): ours lowers at the rise's speed, a frame at a time, so both depend
        // on the frame rate.
        for (int i = 0; i < _lift.Length; i++)
        {
            int lift = IsAtMap && Hovered.Value == i + 1 ? Math.Min(0x48, _lift[i] + 8) : Math.Max(0, _lift[i] - 8);
            if (lift != _lift[i]) { _lift[i] = lift; Revision++; }
        }
    }
    private void ChangeProfile(CampaignProfile profile)
    { Profile = profile; ProfileChanged?.Invoke(profile); Revision++; }
    private void SetPhase(int[] phases, int index, int value)
    { if (phases[index] != value) { phases[index] = value; Revision++; } }
    private static readonly IReadOnlyDictionary<byte, int[]> Routes = new Dictionary<byte, int[]>
    {
        [6] = new[] { 8, 9 }, [7] = new[] { 8, 9, 11 }, [8] = new[] { 8, 9, 11 }, [9] = new[] { 9, 11, 15 },
        [10] = new[] { 11, 13, 15 }, [11] = new[] { 13, 15 }, [12] = new[] { 12, 13, 14, 15, 20 },
        [13] = new[] { 20 }, [14] = new[] { 20 }, [15] = new[] { 20 }, [16] = new[] { 16, 20 },
        [21] = new[] { 21, 22 }, [22] = new[] { 22 },
    };
    private static IReadOnlyList<int> RouteSlots(byte level) => Routes.TryGetValue(level, out int[]? slots) ? slots : Array.Empty<int>();
    public void Dispose()
    { _session.OperationRequested -= OnOperation; _session.Updated -= AdvanceVisuals; }
}
