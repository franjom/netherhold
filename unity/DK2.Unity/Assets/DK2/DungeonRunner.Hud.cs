// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        private readonly ClassicHudControls _hudControls = new ClassicHudControls();
        private BottomPanelView _bottomView;
        private ClassicBottomPanel _bottomPanel;
        private CreatureOverview _overviewView;
        private ClassicCreatureOverview _overviewPanel;
        private RoomNavigation _roomNavigation;
        private ClassicOptionsPanel _optionsPanel;
        private ClassicFullMap _fullMap;
        private BottomPanelChoiceKey? _panelChoice;
        private bool _queryTool;
        private ClassicHudAction _selectedCorner;
        private KeeperResourceSnapshot _resources;
        private IClassicCreatureNames _identityNames;
        private Dk2StringTable _hudStrings;
        private KeeperResourceKind _resourceHoverKind;
        private int _cornerHoverId;
        private HoverTextSnapshot _cornerHover;
        private KeeperResourceSnapshot _resourceHoverFacts;
        private HoverTextSnapshot _resourceHover;
        private readonly HandCursorFeedback _cursorFeedback = new HandCursorFeedback();
        private ClassicCreatureQuery _creatureQuery;
        private ClassicCreatureQueryPanel _queryPanel;
        private CreatureQueryTarget _queryTarget;
        private CreatureQuerySnapshot _querySnapshot = CreatureQuerySnapshot.Empty;

        private HudVisibility CurrentHud => _hudControls.Visibility(_scriptedView.HudFor(cameraPreset));
        private bool HudModal => _hudControls.OptionsVisible || _hudControls.FullMapVisible;

        private void InitializeHudControls(string install, GameDatabase database, bool campaignLevel)
        {
            var bottomAssets = new InstallClassicBottomPanelAssets(install, database);
            _bottomView = new BottomPanelView(database, new PlayerId((byte)viewingPlayerId), bottomAssets.Name, bottomAssets.Order, campaignLevel);
            _bottomPanel = new ClassicBottomPanel(transform, bottomAssets, _bottomView);
            _bottomPanel.SelectCategory(BottomPanelCategory.Creatures);
            _bottomPanel.Selected += SelectPanelChoice;
            _roomNavigation = new RoomNavigation(new PlayerId((byte)viewingPlayerId));
            _bottomPanel.NavigateRoom += NavigateRoom;
            var overviewAssets = new InstallCreatureOverviewAssets(install, database, bottomAssets);
            _overviewView = new CreatureOverview(database, new PlayerId((byte)viewingPlayerId), overviewAssets.Name, overviewAssets.Order);
            _overviewPanel = new ClassicCreatureOverview(transform, overviewAssets, _overviewView);
            _optionsPanel = new ClassicOptionsPanel(transform, new InstallClassicOptionsAssets(install));
            _fullMap = new ClassicFullMap(transform, _minimap);
            _identityNames = new InstallClassicCreatureNames(install);
            _hudStrings = Dk2StringTable.Open(System.IO.Path.Combine(install, "Data", "Text", "Default", "Text.str"));
            _creatureQuery = new ClassicCreatureQuery(database, _hudStrings.Resolve, new PlayerId((byte)viewingPlayerId));
            _queryPanel = new ClassicCreatureQueryPanel(transform, new InstallClassicOptionsAssets(install));
        }

        private void DisposeHudControls()
        {
            _overviewPanel?.Dispose(); _bottomPanel?.Dispose(); _optionsPanel?.Dispose(); _fullMap?.Dispose(); _queryPanel?.Dispose();
        }

        private void SelectPanelChoice(BottomPanelSelection selection)
        {
            _input.CancelPointerGesture();
            _panelChoice = selection.Choice;
            _queryTarget = CreatureQueryTarget.None;
            _queryTool = false; _selectedCorner = ClassicHudAction.None;
            tool = selection.Tool == PlayerToolMode.BuildRoom ? DungeonToolMode.BuildRoom
                : selection.Tool == PlayerToolMode.CreateImp ? DungeonToolMode.CreateImp : DungeonToolMode.Hand;
            if (selection.Tool == PlayerToolMode.BuildRoom) selectedRoomId = selection.Room;
        }

        private void NavigateRoom(RoomTypeId type)
        {
            var rooms = RoomIndex.Build(_simulation.World, _simulation.Database);
            var entries = new System.Collections.Generic.List<RoomNavigationEntry>(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomInstance room = rooms[i];
                if (room.Definition.TypeId is not RoomTypeId roomType) continue;
                TilePos identity = _simulation.World.TilePosition(room.TileIndices[0]);
                entries.Add(new RoomNavigationEntry(identity, new PlayerId(room.OwnerPlayerId), roomType,
                    new TilePos(room.CentreFieldX, room.CentreFieldY)));
            }
            if (_roomNavigation.Next(entries, type) is TilePos tile) StartGlide(tile);
        }

        private void ApplyHudAction(ClassicHudAction action)
        {
            if (action == ClassicHudAction.None) return;
            _input.CancelPointerGesture(); _bottomPanel?.CancelPointerGesture();
            if (action == ClassicHudAction.NavigateHeart)
            {
                if (_worldView.MinimapHeartTile is TilePos heart) StartGlide(heart);
                return;
            }
            if (action == ClassicHudAction.ZoomIn || action == ClassicHudAction.ZoomOut)
            {
                _minimap.HandleZoomPress(Input.mousePosition, action == ClassicHudAction.ZoomIn, action == ClassicHudAction.ZoomOut);
                return;
            }
            if (action == ClassicHudAction.SelectQuery || action == ClassicHudAction.SelectSell)
            {
                _panelChoice = null; _selectedCorner = action;
                _queryTool = action == ClassicHudAction.SelectQuery;
                tool = _queryTool ? DungeonToolMode.Hand : DungeonToolMode.SellRoom;
                _bottomView.SelectTool(ActiveTool, selectedRoomId);
                return;
            }
            _hudControls.Apply(action);
        }

        private void UpdateHudControls(bool allowOptionsKey)
        {
            HudVisibility camera = _scriptedView.HudFor(cameraPreset);
            if (camera.AllowOrdinaryInput)
            {
                if (allowOptionsKey && Input.GetKeyDown(KeyCode.Escape) && !_hudControls.FullMapVisible)
                    ApplyHudAction(ClassicHudAction.ToggleOptions);
                else if (!_hudControls.OptionsVisible && Input.GetKeyDown(KeyCode.M)) ApplyHudAction(ClassicHudAction.ToggleFullMap);
                else if (!_hudControls.FullMapVisible && !_hudControls.OptionsVisible)
                {
                    if (Input.GetKeyDown(KeyCode.G)) ApplyHudAction(ClassicHudAction.ToggleGui);
                    if (Input.GetKeyDown(KeyCode.H)) ApplyHudAction(ClassicHudAction.NavigateHeart);
                }
            }
            HudVisibility hud = CurrentHud;
            _bottomPanel?.SetVisibility(new HudVisibility(hud.ShowNormalHud, hud.AllowOrdinaryInput && !HudModal));
            _optionsPanel?.SetVisible(camera.ShowNormalHud && _hudControls.OptionsVisible && !_hudControls.FullMapVisible);
            _fullMap?.SetVisible(camera.ShowNormalHud && _hudControls.FullMapVisible);
            _queryPanel?.Draw(_querySnapshot, hud, HudModal);
            _overviewPanel?.Draw(hud, _bottomPanel.Category == BottomPanelCategory.Creatures && !HudModal, Input.mousePosition);
        }

        private bool QueueHudInput()
        {
            if (_hudControls.FullMapVisible)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    TilePos tile = _fullMap.ResolvePointer(Input.mousePosition);
                    _cameraFocus = new Vector3(Mathf.Clamp(tile.X + .5f, 0, _cameraBounds.x - 1f / Fp32.One), _cameraFocus.y,
                        Mathf.Clamp(-tile.Y - .5f, -_cameraBounds.y + 1f / Fp32.One, 0));
                    _glideMilliseconds = 0;
                    ApplyHudAction(ClassicHudAction.ToggleFullMap);
                }
                else if (Input.GetMouseButtonDown(1)) ApplyHudAction(ClassicHudAction.ToggleFullMap);
                return true;
            }
            if (_hudControls.OptionsVisible)
            {
                if (Input.GetMouseButtonDown(0) && _optionsPanel.ActionAt(Input.mousePosition) == ClassicOptionsAction.ContinueGame)
                    ApplyHudAction(ClassicHudAction.ToggleOptions);
                return true;
            }
            bool primary = Input.GetMouseButtonDown(0), secondary = Input.GetMouseButtonDown(1);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (_overviewPanel.TryPress(Input.mousePosition, primary, secondary, shift && !control && !alt,
                control && !shift && !alt, out CreatureOverviewRequest request))
            {
                CreatureOverviewAction selected = _overviewView.Select(_simulation.World, request.Type, request.Selection, request.Filter);
                if (selected.Kind == CreatureOverviewActionKind.Pickup)
                {
                    var command = new PickUpCreatureCommand(new PlayerId((byte)viewingPlayerId), selected.Target.Value);
                    _pendingCommands.Add(command);
                    _worldView.QueueMinimapPickupPreview(command, _simulation.World.CurrentTick.Next);
                    _overviewView.QueuePickupPreview(selected.Target, _simulation.World.CurrentTick.Next);
                }
                else if (selected.Kind == CreatureOverviewActionKind.Focus)
                {
                    _cameraFocus = new Vector3(selected.Position.X.Raw / (float)Fp32.One, _cameraFocus.y,
                        -selected.Position.Y.Raw / (float)Fp32.One);
                    _glideMilliseconds = 0;
                }
                return true;
            }
            ClassicHudAction corner = _minimap.HandleCornerPress(Input.mousePosition, primary, secondary);
            if (corner != ClassicHudAction.None) { ApplyHudAction(corner); return true; }
            if (secondary && _minimap.TryNavigate(Input.mousePosition, Camera.main, _cameraFocus, out TilePos _))
            { ApplyHudAction(ClassicHudAction.ToggleFullMap); return true; }
            KeeperResourceKind resource = _resourceHud.ResourceAt(Input.mousePosition);
            if (resource == KeeperResourceKind.Gold && primary)
            {
                KeeperResourceModifiers modifiers = KeeperResourceModifiers.None;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) modifiers |= KeeperResourceModifiers.Shift;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) modifiers |= KeeperResourceModifiers.Control;
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) modifiers |= KeeperResourceModifiers.Alt;
                _pendingCommands.Add(KeeperResourceInteraction.Withdraw(new PlayerId((byte)viewingPlayerId), modifiers));
                _panelChoice = null; _queryTool = false; tool = DungeonToolMode.Hand;
                _bottomView.SelectTool(PlayerToolMode.Hand);
                return true;
            }
            if (resource == KeeperResourceKind.Heart && secondary)
            { ApplyHudAction(ClassicHudAction.NavigateHeart); return true; }
            return false;
        }

        private bool QueuePanelWorldAction()
        {
            if (_queryTool)
            {
                if (_queryTarget == CreatureQueryTarget.None && Input.GetMouseButtonDown(0) && !PointerOverHud(Input.mousePosition)
                    && ScreenToTile(Input.mousePosition) is TilePos queryTile)
                {
                    WorldHoverHit hit = ResolveWorldHover(queryTile);
                    if (hit.Kind == WorldHoverKind.Creature)
                    {
                        _queryTarget = new CreatureQueryTarget(hit.EntityId); _glideMilliseconds = 0;
                        if (_classicCamera != null && Camera.main != null)
                        {
                            var focus = new System.Numerics.Vector3(_cameraFocus.x, _cameraFocus.y, _cameraFocus.z);
                            var viewport = new CameraViewport(Screen.width, Camera.main.pixelWidth, Camera.main.pixelHeight);
                            Dk2Angle yaw = new Dk2Angle(unchecked((ushort)Mathf.RoundToInt(_classicCamera.Pose(focus, viewport).YawDegrees * 2048 / 360f)));
                            Dk2Angle desired = ClassicCreatureQueryRules.FollowHeading(yaw);
                            _classicCamera.Rotate(new Dk2Angle(unchecked((ushort)(desired.Value - yaw.Value))));
                        }
                    }
                }
                if (Input.GetMouseButtonDown(1))
                {
                    _queryTarget = CreatureQueryTarget.None; _queryTool = false;
                    _selectedCorner = ClassicHudAction.None; tool = DungeonToolMode.Hand;
                }
                return true;
            }
            if (Input.GetMouseButtonDown(1) && (tool != DungeonToolMode.Hand || _panelChoice != null))
            {
                if (_input.DragFrom(ActiveTool) != null) _input.CancelPointerGesture();
                else
                {
                    _panelChoice = null; tool = DungeonToolMode.Hand; _selectedCorner = ClassicHudAction.None;
                    _bottomView.SelectTool(PlayerToolMode.Hand);
                }
                return true;
            }
            if (_panelChoice is not BottomPanelChoiceKey choice || choice.Kind == BottomPanelChoiceKind.Room
                || choice.Kind == BottomPanelChoiceKind.Spell && choice.Value == 4) return false;
            if (!Input.GetMouseButtonDown(0) || PointerOverHud(Input.mousePosition)) return true;
            if (ScreenToTile(Input.mousePosition) is not TilePos tile) return true;
            PlayerId viewer = new PlayerId((byte)viewingPlayerId);
            if (choice.Kind == BottomPanelChoiceKind.Door) _pendingCommands.Add(new PlaceDoorCommand(viewer, tile, choice.Value));
            else if (choice.Kind == BottomPanelChoiceKind.Trap) _pendingCommands.Add(new PlaceTrapCommand(viewer, tile, choice.Value));
            else
            {
                KeeperSpellDef spell = _simulation.Database.KeeperSpellById(choice.Value);
                if (spell == null) return true;
                if (KeeperSpellRules.TargetsAThing(spell.TargetSelector))
                {
                    WorldHoverHit hit = ResolveWorldHover(tile);
                    if (hit.Kind == WorldHoverKind.Creature)
                        _pendingCommands.Add(new CastSpellOnCreatureCommand(viewer, choice.Value, hit.EntityId));
                }
                else _pendingCommands.Add(new CastSpellAtPlaceCommand(viewer, choice.Value,
                    CreatureState.CentreOf(tile.X), CreatureState.CentreOf(tile.Y)));
            }
            return true;
        }

        private void DrawHudControls()
        {
            _resources = _resourceView.Capture(_simulation.World, displayHealth: CurrentHud.ShowNormalHud);
            _bottomView.Capture(_simulation.World);
            _bottomPanel.Draw();
            _overviewView.Capture(_simulation.World);
            _overviewPanel.Draw(CurrentHud, _bottomPanel.Category == BottomPanelCategory.Creatures && !HudModal, Input.mousePosition);
            _minimap.UpdateControls(Input.mousePosition, _selectedCorner, _hudControls.OptionsVisible);
            _optionsPanel.Draw(Input.mousePosition); _fullMap.Draw();
            CreatureState creature = _simulation.World.CreatureById(_queryTarget.Value);
            string name = creature == null ? "" : _identityNames.Name(new CreatureNameIndex(creature.NameIndex));
            string blood = creature == null ? "" : _identityNames.BloodGroup(new CreatureBloodGroupIndex(creature.BloodGroupIndex));
            _querySnapshot = _creatureQuery.Capture(_simulation.World, _queryTarget, name, blood);
            _queryTarget = _querySnapshot.Target;
            _queryPanel.Draw(_querySnapshot, CurrentHud, HudModal);
        }

        private bool HoldingGold()
        {
            var hand = _simulation.World.Player(new PlayerId((byte)viewingPlayerId)).Hand;
            if (hand.Count == 0) return false;
            HandItem top = hand[hand.Count - 1];
            return top.Kind == HandItemKind.Object && _simulation.World.ObjectById(top.Id) is ObjectState obj
                && obj.ObjectTypeId is 1 or 2 or 3;
        }

        private void FollowQueriedCreature()
        {
            if (!_queryTool || _queryTarget == CreatureQueryTarget.None || _scriptedView.PathPlaying || HudModal) return;
            if (_scene.TryGetCreaturePosition(_queryTarget, out Vector3 position))
            {
                _cameraFocus = new Vector3(position.x, _cameraFocus.y, position.z);
                if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
            }
        }

        private HoverTextSnapshot CaptureHudHover()
        {
            if (_overviewPanel != null && _overviewPanel.HoveredTextId(Input.mousePosition) is int widget && widget != 0)
            {
                if (_cornerHoverId != widget) { _cornerHoverId = widget; _cornerHover = HudWidgetText.Hover(widget, _hudStrings.Resolve); }
                return _cornerHover;
            }
            if (_bottomPanel.ContainsPointer(Input.mousePosition)) return _bottomPanel.HoverText;
            if (_minimap != null && HudWidgetText.ForAction(_minimap.HoveredControl(Input.mousePosition)) is int corner)
            {
                if (_cornerHoverId != corner) { _cornerHoverId = corner; _cornerHover = HudWidgetText.Hover(corner, _hudStrings.Resolve); }
                return _cornerHover;
            }
            KeeperResourceKind kind = _resourceHud.ResourceAt(Input.mousePosition);
            if (kind == KeeperResourceKind.None) return HoverTextSnapshot.Empty;
            if (_resourceHoverKind != kind || _resourceHoverFacts != _resources)
            {
                _resourceHoverKind = kind; _resourceHoverFacts = _resources;
                _resourceHover = KeeperResourceInteraction.ResourceHover(_resources, kind, _hudStrings.Resolve);
            }
            return _resourceHover;
        }
    }
}
