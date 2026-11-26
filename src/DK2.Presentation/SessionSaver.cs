// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Writes the running session to the save browser's folder under its typed name: the campaign carry goes into
/// the profile first, then the header, the exact world checkpoint (<see cref="WorldSnapshot.ToBytes"/>) and that profile
/// go into one <see cref="SavedSessionFile"/>. The adapter supplies the profile load used when none is loaded yet and the
/// UTC clock the header is stamped from.</summary>
public sealed class SessionSaver
{
    private readonly CampaignProfileStore _profiles;
    private readonly Action _loadProfile;
    private readonly Func<DateTime> _utcClock;

    public SessionSaver(CampaignProfileStore profiles, Action loadProfile, Func<DateTime> utcClock)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _loadProfile = loadProfile ?? throw new ArgumentNullException(nameof(loadProfile));
        _utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
    }

    /// <summary>Saves <paramref name="simulation"/>, which must be quiet: an adapter joins its tick worker first.
    /// <paramref name="carry"/> is the world's carry as the adapter reads it (equal to the live world's once no tick is in
    /// flight). Refreshes <paramref name="saves"/> after the write and returns the file written.</summary>
    public string Save(GameSimulation simulation, CampaignCarry carry, SessionContext context, SaveSlotsView saves)
    {
        if (simulation is null)
        {
            throw new ArgumentNullException(nameof(simulation));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (saves is null)
        {
            throw new ArgumentNullException(nameof(saves));
        }

        if (_profiles.Profile == null)
        {
            _loadProfile();
        }

        if (context.PlayedSlot != CampaignSlot.None)
        {
            context.ApplyCarry(_profiles, carry);
        }

        string path = SavedSessionFile.PathForName(saves.Directory, saves.Name);
        var header = new SavedSessionHeader(context.LevelName, (byte)context.Mode, (ushort)context.ReturnPage,
            _utcClock(), saves.Name);
        SavedSessionFile.Save(path, new SavedSession(header, WorldSnapshot.ToBytes(simulation), _profiles.Profile!));
        saves.Refresh();
        return path;
    }
}
