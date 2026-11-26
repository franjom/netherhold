// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The front end's 3D station (Unity's runner in its front-end mode over <c>DungeonScene</c>): the front-end level's
/// world with the campaign lands and markers (<see cref="FrontEndArt"/>), seen from the front end's camera paths
/// (<see cref="FrontEndPose"/>) instead of the classic camera, and the map pick (<see cref="PickFrontEndMap"/>).</summary>
public partial class WorldScene
{
    /// <summary>The front end's camera this frame (<see cref="FrontEndSession.Pose"/>), drawn at the next
    /// <see cref="PlaceCamera"/>; null holds the last pose. Read only in a scene opened by <see cref="OpenFrontEnd"/>.</summary>
    public CameraPathPose? FrontEndPose { get; set; }

    /// <summary>Builds the station's world from <paramref name="station"/>'s view, with the front end's art added to the
    /// install's, and a camera the front end's paths place.</summary>
    public void OpenFrontEnd(string install, CampaignLevelData level, FrontEndStation station, PlayerId viewer)
    {
        OpenWorld(install, level, station.Simulation.World, station.View, viewer, FrontEndArt.Entries(level.Database));
        OpenCamera();
    }

    /// <summary>Unity's <c>ApplyFrontEndCamera</c>: the path's place and turn, the front end's 4:3 vertical field of view
    /// (<see cref="CameraPathPose.FrontEndVerticalFieldOfView"/>), the path's view distance as the far plane, no centre shift.</summary>
    private void PlaceFrontEndCamera(CameraViewport viewport)
    {
        if (FrontEndPose is not CameraPathPose path) return;
        var pose = new ClassicCameraPose(path.Position, path.Forward, path.Up,
            path.FrontEndVerticalFieldOfView(viewport.Width, viewport.Height), path.ViewDistance, 0, 0);
        PlacePose(pose, viewport, NearPlane);
    }

    /// <summary>Unity's <c>DungeonScene.PickFrontEndMap</c>: the nearest triangle the ray crosses among the drawn objects whose
    /// box it enters, and the slot of that object in <paramref name="slots"/> (<see cref="CampaignSlot.None"/> for an object
    /// that is no map piece or marker, or for nothing). The ray is in the renderers' frame (<see cref="TryPickRay"/>).
    /// Allocation-free.</summary>
    public CampaignSlot PickFrontEndMap(NVector3 origin, NVector3 direction, IReadOnlyDictionary<int, CampaignSlot> slots)
    {
        float nearest = float.PositiveInfinity;
        CampaignSlot result = CampaignSlot.None;
        foreach (KeyValuePair<int, ModelSlot> entry in _objects)
        {
            ModelSlot slot = entry.Value;
            if (!slot.Node.Visible) continue;
            slot.Measure(out PickBox bounds, out PickSphere? _);
            if (bounds.RayEntry(origin, direction) == null) continue;
            if (slot.NearestTriangle(origin, direction, nearest) is not float distance || distance >= nearest) continue;
            nearest = distance;
            result = slots.TryGetValue(entry.Key, out CampaignSlot picked) ? picked : CampaignSlot.None;
        }
        return result;
    }
}
