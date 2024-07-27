using HarmonyLib;
using UI.Builder;
using UI.CarInspector;
using JetBrains.Annotations;
using Model.OpsNew;
using Network;
using System;
using Model;
using Track;
using Game.Messages;
using Game.State;
using System.Linq;
using SmartOrders.HarmonyPatches;
using System.Diagnostics.CodeAnalysis;
using SwitchToDestination;
using UI.Common;
using UnityEngine;
using Game;
using Network.Messages;


[PublicAPI]
[HarmonyPatch]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class CarInspectorPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "PopulateWaybillPanel", new Type[] { typeof(UIPanelBuilder), typeof(Waybill) })]
    static void PopulateWaybillPanel(UIPanelBuilder builder, Waybill waybill, CarInspector __instance, Car? ____car)
    {
        if (!SwitchToDestinationPlugin.Shared.IsEnabled)
        {
            return;
        }

        builder.AddButton("Open switches to destination", delegate
        {
            OpenSwitchesToDestination(____car, waybill.Destination, 0, true);
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CarInspector), "PopulatePanel")]
    public static void PopulatePanel(UIPanelBuilder builder, Car? ____car, Window ____window)
    {
        if (!SwitchToDestinationPlugin.Shared.IsEnabled)
        {
            return;
        }

        var size = ____window.GetContentSize();

        ____window.SetContentSize(new Vector2(size.x - 2, 322 + 70));
    }


    private static void OpenSwitchesToDestination(Car car, OpsCarPosition destination, int span, bool routeToOtherTrackIfNoSwitchesMoved)
    {
        DebugLog($"Checking switches for route to {destination.DisplayName} for car {car.DisplayName}");

        TrainController shared = TrainController.Shared;
        Graph graph = shared.graph;

        DebugLog($"Found {destination.Spans.Length} tracks at destination");

        var detinationSpan = destination.Spans[span];
        Location destinationLocation;

        if (detinationSpan.lower != null)
        {
            destinationLocation = detinationSpan.lower.Value;
        }
        else if (detinationSpan.upper != null)
        {
            destinationLocation = detinationSpan.upper.Value;
        }
        else
        {
            destinationLocation = Location.Invalid;
            DebugLog("ERROR: couldn't find valid destination location");
        }

        var route = graph.FindRoute(car.LocationB, destinationLocation);

        DebugLog($"Route found with {route.Count} segments");

        int switchCount = 0;
        int movedSwitchCount = 0;
        bool stoppedDueToSwitchback = false;
        for (int i = 0; i < route.Count - 1; i++)
        {
            var nearSegment = route[i];
            var farSegment = route[i + 1];

            // need to find which node has the route to the next segment
            TrackNode node = nearSegment.NodeForEnd(TrackSegment.End.B);
            if (!farSegment.Contains(node))
            {
                node = nearSegment.GetOtherNode(node);
            }

            if (!graph.IsSwitch(node))
            {
                continue;
            }

            switchCount++;

            bool switchAgainstMovement = false;

            // if at least one of the segment is the entrance track
            // Check for switch against movement exception in both directions. If we get an error in either case then we need to throw the switch
            if (node.SegmentCanReachSegment(nearSegment, farSegment))
            {
                DebugLog($"Route via {node.name} is direct");
                try
                {
                    graph.CheckSwitchAgainstMovement(nearSegment, farSegment, node);
                }
                catch (SwitchAgainstMovement)
                {
                    DebugLog($"Switch {node.name} is against movement (near->far)");
                    switchAgainstMovement = true;
                }

                try
                {
                    graph.CheckSwitchAgainstMovement(farSegment, nearSegment, node);
                }
                catch (SwitchAgainstMovement)
                {
                    DebugLog($"Switch {node.name} is against movement (far->near)");
                    switchAgainstMovement = true;
                }
            }
            else
            {
                DebugLog($"Route via {node.name} requires changing direction");
                stoppedDueToSwitchback = true;

                // otherwise we need to find the third segment, and try routing to that
                var thirdSegment = graph.SegmentsConnectedTo(node).First((segment) => segment.id != nearSegment.id && segment.id != farSegment.id);
                if (thirdSegment == null)
                {
                    DebugLog($"ERROR: unable to find third segment connected to node {node.name}");
                    continue;
                }

                try
                {
                    graph.CheckSwitchAgainstMovement(nearSegment, thirdSegment, node);
                }
                catch (SwitchAgainstMovement)
                {
                    DebugLog($"Switch {node.name} is against movement (near->switchback)");
                    switchAgainstMovement = true;
                }

                break;
            }

            if (switchAgainstMovement)
            {
                var statusStr = (!node.isThrown) ? "thrown" : "not thrown";
                DebugLog($"Setting switch {node.name} to {statusStr}");
                StateManager.ApplyLocal(new RequestSetSwitch(node.id, !node.isThrown));
                movedSwitchCount++;
            }
            else
            {
                var statusStr = (node.isThrown) ? "thrown" : "not thrown";
                DebugLog($"Switch {node.name} is not blocking movement, already {statusStr}");
            }
        }

        if(switchCount == 0)
        {
            Say($"No switches found between car {car.DisplayName} and {destination.DisplayName}, nothing to do");
            return;
        } 

        if (movedSwitchCount == 0)
        {
            if (destination.Spans.Length > 1 && !stoppedDueToSwitchback && routeToOtherTrackIfNoSwitchesMoved)
            {
                // if we didn't have to move anything, and there is another possible span then try the other span
                var nextSpan = (span + 1) % destination.Spans.Length;
                Say($"Switches already set for track {span + 1} at {destination.DisplayName}, routing to track {nextSpan + 1} instead");
                OpenSwitchesToDestination(car, destination, nextSpan, false);

                return;
            }
        }

        string switchesSetStr;

        if (movedSwitchCount == 0) {
            switchesSetStr = $"{switchCount} switches already set correctly";
        } else
        {
            switchesSetStr = $"Moved {movedSwitchCount} out of {switchCount} switches";
        }

        string message;
        if (stoppedDueToSwitchback)
        {
            message = $"{switchesSetStr} to clear route for {car.DisplayName} up to first direction change to get to {destination.DisplayName}";
        } else
        {
            string destinationName = destination.DisplayName;
            if (destination.Spans.Length > 0)
            {
                destinationName = $"{destinationName} track {span + 1}";
            }

            message = $"{switchesSetStr} to clear route for {car.DisplayName} to go to {destinationName}";
        }

        Say(message);
    }

    private static void Say(string message)
    {
        Alert alert = new Alert(AlertStyle.Console, message, TimeWeather.Now.TotalSeconds);
        WindowManager.Shared.Present(alert);
    }

    private static void DebugLog(string message)
    {
        if (!SwitchToDestinationPlugin.Settings.EnableDebug)
        {
            return;
        }

        Say(message);
    }
}