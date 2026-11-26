// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
namespace DK2.Presentation;
/// <summary>Lets Level6a's opening death tutorial speak without the generic torture announcements.</summary>
public sealed class Level6aTortureSpeech(GameDatabase database, MessageTabsView messages,
    ISimulationEventSink downstream) : ISimulationEventSink
{
    private static readonly KwdTriggerId OpeningGuardTree = new(24);
    private static readonly CreatureTypeId Guard = new(25);
    public void Emit(in SimEvent simEvent)
    {
        // Requested presentation deviation; see ADR-0809. The simulation keeps both notifications.
        if (string.Equals(database.Level?.Name, "Level6a.kwd", StringComparison.OrdinalIgnoreCase)
            && simEvent.TortureMessage is { } notice && notice.Trigger == OpeningGuardTree && notice.CreatureType == Guard
            && notice.Message.PlayerId == PlayerIds.FirstKeeper
            && notice.Message.Kind is PlayerMessageKind.InformationLearned or PlayerMessageKind.InformationReveal)
        {
            messages.SuppressSpeech(notice.Message);
        }

        downstream.Emit(simEvent);
    }
}
