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
    private TimeSpan _entered, _updated;
    private bool _held;
    private int _blink;
    public CampaignMapController(FrontEndSession session, CampaignProfile profile, bool secretFiveAvailable,
        Func<CampaignSlot, CampaignBriefing> loadBriefing)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _loadBriefing = loadBriefing ?? throw new ArgumentNullException(nameof(loadBriefing));
        Profile = CampaignProgression.EnsureOpenMap(profile ?? throw new ArgumentNullException(nameof(profile)));
        SecretFiveAvailable = secretFiveAvailable;
        _session.OperationRequested += OnOperation;
        _session.Updated += AdvanceVisuals;
    }
    public CampaignProfile Profile { get; private set; }
    public bool SecretFiveAvailable { get; }
    public CampaignBriefing? Briefing { get; private set; }
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
    private bool IsMapStation => IsAtMap || _session.Page == FrontEndPage.Briefing || _session.Path is FrontEndPath.EntryToTable or FrontEndPath.TableToBriefing or FrontEndPath.BriefingToTable;
    private void EnterMap()
    {
        _entered = _updated = _session.Now; _blink = 0xfff; _introPending = true; Hovered = CampaignSlot.None;
        Array.Clear(_markerPhases, 0, _markerPhases.Length);
        for (int i = 0; i < 30; i++) _landPhases[i] = i < 25 && Profile.PlayerLevelStatus[i]
            is CampaignLevelStatus.Won or CampaignLevelStatus.ClosedBranch ? 0x55a : 0;
        Revision++;
    }
    private void OnOperation(FrontEndOperation operation)
    {
        if (operation == FrontEndOperation.EnterCampaignMap) EnterMap();
        if (operation is FrontEndOperation.CampaignMapFrame or FrontEndOperation.EnterCampaignMap)
        {
            _session.TryShowPage(FrontEndPage.CampaignMap);
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
        int returnSteps = (int)(_session.Now.TotalSeconds * SimulationTiming.OriginalTicksPerGameSecond) - (int)(_updated.TotalSeconds * SimulationTiming.OriginalTicksPerGameSecond);
        for (int i = 0; i < _lift.Length; i++)
        {
            int lift = IsAtMap && Hovered.Value == i + 1 ? Math.Min(0x48, _lift[i] + 8) : Math.Max(0, _lift[i] - returnSteps * 8);
            if (lift != _lift[i]) { _lift[i] = lift; Revision++; }
        }
        _updated = _session.Now;
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
