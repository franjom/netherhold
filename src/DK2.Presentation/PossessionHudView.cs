// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;

namespace DK2.Presentation;
/// <summary>The independent possession carry slot, player +0x136; ordinary keeper hand contents do not supply it.</summary>
public enum PossessionCarryState { Empty, HoldingThing }

/// <summary>The possession HUD's facts from the settled world, independent of an engine.</summary>
public sealed class PossessionHudView
{
    private readonly GameDatabase _database;
    private readonly InstallPossessionHudArt _art;
    private string? _creatureOverlay;
    public PossessionHudView(GameDatabase database, InstallPossessionHudArt art) { _database = database; _art = art; }

    public PossessionHudSnapshot? Capture(WorldState world, PlayerId viewer, PossessionControls controls,
        PossessionCarryState carry = PossessionCarryState.Empty)
    {
        CreatureState? creature = world.CreatureById(world.Player(viewer).PossessedCreatureId);
        if (creature == null || creature.OwnerPlayerId != viewer) return null;
        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        CreatureOverviewType type = new(creature.CreatureTypeId);
        var icons = new List<PossessionHudIcon>();
        int meleeTicks = CombatRules.RechargeTicks(_database, creature, CombatRules.TicksOf(definition.MeleeRecharge));
        icons.Add(new(_art.Melee(type), Recharge(world.CurrentTick, creature.TimeLastUsedMelee, meleeTicks), controls.Melee));
        for (int slot = 0; slot < Math.Min(3, definition.Spells.Count); slot++)
        {
            CreatureSpellSlot available = definition.Spells[slot];
            if (available.CreatureSpellId == 0 || available.LevelAvailable > creature.Level) continue;
            CreatureSpellDef spell = _database.CreatureSpells[available.CreatureSpellId];
            int interval = CombatRules.RechargeTicks(_database, creature, SpellRules.RechargeTicks(spell));
            icons.Add(new(spell.GuiIcon.Name, Recharge(world.CurrentTick, creature.SpellStamps[slot], interval),
                !controls.Melee && controls.Slot == slot, Spell: true));
        }
        string special = (byte)definition.PrimaryAbility switch
        {
            1 => "throw-mode", 2 => "sniper-mode", 3 => "bat-mode", 6 => "sneak-mode", 7 => "pray-mode", _ => "",
        };
        if ((byte)definition.PrimaryAbility == 1) icons.Add(ThrowIcon(carry, (controls.Selection & 2) != 0));
        else if (special.Length != 0)
            icons.Add(new("GUI\\Icons\\1st-person\\" + special, 100, (byte)definition.PrimaryAbility switch
            {
                2 => creature.SniperMode, 3 => creature.PossessionFlightForm,
                6 => creature.SneakingAs != 0, 7 => creature.PossessionPraying, _ => false,
            }));
        if ((byte)definition.SecondaryAbility == 5)
            icons.Add(new("GUI\\Icons\\1st-person\\hypnotise-mode", 100,
                creature.HypnotisedCreatureId != 0));
        icons.Add(new("GUI\\Icons\\1st-person\\group-mode", 100, !controls.Melee && controls.Slot == 3));
        var followers = new List<string>();
        foreach (CreatureState other in world.OwnedCreatures(viewer))
            if (other.FollowLeaderId != 0) followers.Add(_art.Portrait(new(other.CreatureTypeId)));
        if (creature.CreatureTypeId != 0)
            _creatureOverlay = creature.CreatureTypeId switch
            {
                13 or 28 => "GUI\\Filters\\F-Knight",
                22 => "GUI\\Filters\\F-Black_Knight",
                25 or 30 => "GUI\\Filters\\F-Guard",
                12 => "GUI\\Filters\\F-FireFly",
                _ => null,
            };
        var overlays = new List<string>(3);
        if (creature.SniperMode) overlays.Add("GUI\\Filters\\F-Sniper");
        if (_creatureOverlay != null) overlays.Add(_creatureOverlay);
        if (creature.FrozenTicks != 0) overlays.Add("GUI\\Filters\\F-Frozen");
        int maxHealth = CreatureHealthRules.MaxHealth(_database, definition, creature);
        return new(_art.Icon(type), maxHealth <= 0 ? 0 : (int)Math.Min(100L, 100L * Math.Max(0, creature.Health) / maxHealth),
            new(creature.GoldHeld), icons, followers) { Overlays = overlays };
    }
    public static PossessionHudIcon ThrowIcon(PossessionCarryState carry, bool selected) =>
        new("GUI\\Icons\\1st-person\\" + (carry == PossessionCarryState.HoldingThing ? "throw_imp-mode"
            : selected ? "throw_book-mode" : "throw-mode"), 100, selected || carry == PossessionCarryState.HoldingThing);

    /// <summary>The HUD recharge getters omit the combat work-rate divisor (0x0048CA10, 0x0048C8B0).</summary>
    public static int Recharge(GameTick now, long stamp, int intervalTicks)
    {
        if (stamp == CreatureState.NeverUsed || intervalTicks <= 0 || now.Value - stamp >= intervalTicks) return 100;
        int reciprocal = (int)((100L << 24) / ((long)intervalTicks << 12));
        return Math.Clamp((int)((now.Value - stamp) * reciprocal >> 12), 0, 100);
    }
}
