// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>What the HUD's panels report under the pointer, for <see cref="HudHoverText.Capture"/>. An adapter answers
/// for the panels it has and gives the empty answer (0, null, false, <see cref="ClassicHudAction.None"/>,
/// <see cref="KeeperResourceKind.None"/>) for the rest. Each member is asked only when every source before it in
/// <see cref="HudHoverText.Capture"/>'s order had nothing, so an answer can read the pointer itself.</summary>
public interface IHudHoverSources
{
    /// <summary>The creature overview's hovered widget text id (its mode selectors and worker icons), 0 for none.</summary>
    int OverviewTextId();
    /// <summary>The creature overview's hovered portrait column, or null.</summary>
    CreatureOverviewType? OverviewColumn();
    /// <summary>The hover text for a creature type's portrait; asked only when the hovered column changes.</summary>
    HoverTextSnapshot CreatureTypeText(CreatureOverviewType column);
    bool OverMessageTabs();
    HoverTextSnapshot MessageTabsText();
    bool OverBattleTab();
    HoverTextSnapshot BattleTabText();
    bool OverBottomPanel();
    HoverTextSnapshot BottomPanelText();
    /// <summary>The minimap corner control under the pointer, or <see cref="ClassicHudAction.None"/>.</summary>
    ClassicHudAction MinimapControl();
    /// <summary>The resource strip's widget under the pointer, or <see cref="KeeperResourceKind.None"/>.</summary>
    KeeperResourceKind Resource();
}

/// <summary>The status line and tooltip while the pointer is over the HUD: the first panel with something under the
/// pointer, in the order Unity's runner asked them (moved unchanged from <c>DungeonRunner.CaptureHudHover</c>):
/// the creature overview's widgets, then its portraits, the message tabs, the battle tab, the bottom panel, the
/// minimap's corners and last the resource strip. Widget, portrait and resource texts are kept until what they were
/// built from changes, so a still pointer builds no text.</summary>
public sealed class HudHoverText
{
    private readonly Func<uint, string> _resolve;
    private int _cornerId;
    private HoverTextSnapshot _corner;
    private CreatureOverviewType? _portraitType;
    private HoverTextSnapshot _portrait;
    private KeeperResourceKind _resourceKind;
    private KeeperResourceSnapshot _resourceFacts;
    private HoverTextSnapshot _resource;

    /// <param name="resolve">A <c>Text.str</c> string by id.</param>
    public HudHoverText(Func<uint, string> resolve) => _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));

    /// <param name="hud">The panels under the pointer.</param>
    /// <param name="resources">The keeper's resources this frame, for the resource strip's texts.</param>
    public HoverTextSnapshot Capture(IHudHoverSources hud, in KeeperResourceSnapshot resources)
    {
        if (hud is null) throw new ArgumentNullException(nameof(hud));
        int widget = hud.OverviewTextId();
        if (widget != 0) return Widget(widget);
        if (hud.OverviewColumn() is CreatureOverviewType column)
        {
            if (_portraitType != column)
            {
                _portraitType = column;
                _portrait = hud.CreatureTypeText(column);
            }
            return _portrait;
        }
        if (hud.OverMessageTabs()) return hud.MessageTabsText();
        if (hud.OverBattleTab()) return hud.BattleTabText();
        if (hud.OverBottomPanel()) return hud.BottomPanelText();
        if (HudWidgetText.ForAction(hud.MinimapControl()) is int corner) return Widget(corner);
        KeeperResourceKind kind = hud.Resource();
        if (kind == KeeperResourceKind.None) return HoverTextSnapshot.Empty;
        if (_resourceKind != kind || _resourceFacts != resources)
        {
            _resourceKind = kind; _resourceFacts = resources;
            _resource = KeeperResourceInteraction.ResourceHover(resources, kind, _resolve);
        }
        return _resource;
    }

    /// <summary>The overview's widgets and the minimap's corners share one kept text, as the runner's did.</summary>
    private HoverTextSnapshot Widget(int textId)
    {
        if (_cornerId != textId) { _cornerId = textId; _corner = HudWidgetText.Hover(textId, _resolve); }
        return _corner;
    }
}
