namespace SmartOrders.HarmonyPatches;

using System;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Track;

[HarmonyPatch]
public static class GraphPatches
{

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(Graph), "CheckSwitchAgainstMovement")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static void CheckSwitchAgainstMovement(this Graph __instance, TrackSegment seg, TrackSegment nextSegment, TrackNode node)
    {
        throw new NotImplementedException("This is a stub");
    }
}