// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Builds and refreshes the front end's 3D station from the level's world, with no things, scripts or fog
/// (<see cref="GameSessionStarter.FrontEnd"/>), its scenery with the campaign markers (<see cref="FrontEndScenery"/>), the
/// campaign map's pick table (<see cref="FrontEndHost.FillPickSlots"/>), and the view whose scenery follows the map's
/// <see cref="CampaignMapController.Revision"/>. The adapter draws <see cref="View"/> and picks the map pieces by view id.</summary>
public sealed class FrontEndStation
{
    private readonly Dictionary<int, CampaignSlot> _pickSlots = new Dictionary<int, CampaignSlot>();
    private int _revision = -1;

    /// <summary>The station over <paramref name="simulation"/>'s world, its scenery seeded from <paramref name="seed"/> and
    /// the campaign profile's secret lands.</summary>
    public FrontEndStation(GameSimulation simulation, CampaignProfile profile, bool secretFiveAvailable, PlayerId viewer, ulong seed)
    {
        Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        GameDatabase database = simulation.Database;
        IReadOnlyList<FrontEndProp> scenery = FrontEndScenery.Create(simulation.World, database, profile, new SimRandom(seed),
            secretFiveAvailable);
        IReadOnlyList<FrontEndProp> markers = FrontEndScenery.CreateCampaignMarkers(database);
        var all = new FrontEndProp[scenery.Count + markers.Count];
        for (int i = 0; i < scenery.Count; i++)
        {
            all[i] = scenery[i];
        }

        for (int i = 0; i < markers.Count; i++)
        {
            all[scenery.Count + i] = markers[i];
        }

        BaseScenery = Array.AsReadOnly(all);
        View = new WorldView(database, viewer) { FrontEndScenery = BaseScenery };
        FrontEndHost.FillPickSlots(BaseScenery, _pickSlots);
    }

    /// <summary>The station for the front-end level (<see cref="GameSessionRequest.FrontEnd"/>'s), as
    /// <see cref="GameSessionStarter.Campaign"/> starts it with the front end on.</summary>
    public static FrontEndStation Open(CampaignLevelData level, CampaignProfile profile, bool secretFiveAvailable, PlayerId viewer,
        ulong seed)
    {
        if (level is null)
        {
            throw new ArgumentNullException(nameof(level));
        }

        return new FrontEndStation(GameSessionStarter.Campaign(level, resumed: null, frontEnd: true, seed, fogOfWarEnabled: false),
            profile, secretFiveAvailable, viewer, seed);
    }

    public GameSimulation Simulation { get; }

    /// <summary>The world as the station draws it; its <see cref="WorldView.FrontEndScenery"/> is the map's projection.</summary>
    public WorldView View { get; }

    /// <summary>The scenery as created, before the campaign map projects its lands and markers.</summary>
    public IReadOnlyList<FrontEndProp> BaseScenery { get; }

    /// <summary>The campaign slot each map piece and marker picks, by view id.</summary>
    public IReadOnlyDictionary<int, CampaignSlot> PickSlots
    {
        get
        {
            return _pickSlots;
        }
    }

    /// <summary>The slot a picked view id selects, or <see cref="CampaignSlot.None"/> for anything else.</summary>
    public CampaignSlot SlotOf(int viewId)
    {
        return _pickSlots.TryGetValue(viewId, out CampaignSlot slot) ? slot : CampaignSlot.None;
    }

    /// <summary>When the map's revision changes, update the view from the map's projection of the
    /// base scenery (<see cref="FrontEndScenery.ApplyCampaignMap"/>); returns whether it did, so the caller presents again.</summary>
    public bool Refresh(CampaignMapController map)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        if (_revision == map.Revision)
        {
            return false;
        }

        _revision = map.Revision;
        View.FrontEndScenery = FrontEndScenery.ApplyCampaignMap(BaseScenery, map);
        return true;
    }
}
