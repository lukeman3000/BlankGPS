using SonsSdk;
using Sons;
using RedLoader;
using Sons.Gameplay.GPS;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;

namespace BlankGPS;

public class BlankGPS : SonsMod
{
    private static bool _hasDisabledIcons = false; // Flag to stop the mod once all markers are disabled
    private static List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)> _markersToDisable; // List of markers to disable
    private static List<string> _processedMarkers; // Track which markers have been processed
    private static int _disabledMarkerCount = 0; // Count how many markers we've disabled

    public BlankGPS()
    {
        // Initialize the static marker list
        _markersToDisable = new List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)>
        {
            ("CaveAEntranceGPS", "_iconScale", 1.1f, false),
            ("CaveBEntranceGPS", "_iconScale", 1.1f, false),
            ("CaveCEntranceGPS", "_iconScale", 1.1f, false),
            ("CaveDAEntranceGPS", "_iconScale", 1.1f, false),
            ("CaveDBEntranceGPS", "_iconScale", 1.1f, false),
            ("CaveFEntranceGPS", "_iconScale", 1.1f, false),
            ("CaveGAEntranceGPS", "_iconScale", 1.1f, false),
            ("SnowCaveAEntranceGPS", "_iconScale", 1.1f, false),
            ("SnowCaveBEntranceGPS", "_iconScale", 1.1f, false),
            ("SnowCaveCEntranceGPS", "_iconScale", 1.1f, false),
            ("GPSLocatorPickup", "Position", new Vector3(-626.3061f, 145.3385f, 386.1634f), true),
            ("GPSLocatorPickup", "Position", new Vector3(-1340.964f, 95.4219f, 1411.981f), true),
            ("GPSLocatorPickup", "Position", new Vector3(-1797.652f, 14.4886f, 577.0323f), true)
        };
        _processedMarkers = new List<string>(); // Initialize the list to track processed markers
    }

    protected override void OnInitializeMod()
    {
        Config.Init();
        var harmony = new HarmonyLib.Harmony("com.yourname.blankgps");
        harmony.PatchAll();
    }

    protected override void OnSdkInitialized()
    {
        BlankGPSUi.Create();
    }

    protected void Update()
    {
        if (_hasDisabledIcons)
        {
            OnUpdateCallback = null;
            return;
        }
    }

    [HarmonyPatch(typeof(GPSLocator), "OnEnable")]
    public class GPSLocatorAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix(GPSLocator __instance)
        {
            if (!__instance) return; // Safety check
            if (_hasDisabledIcons) return; // Stop if we've disabled all markers

            // Check if this GPSLocator matches one of our target markers
            foreach (var marker in _markersToDisable)
            {
                if (__instance.gameObject.name == marker.gameObjectName)
                {
                    // Use reflection to access the identifier (property or method)
                    object identifierValue = null;
                    if (marker.isMethod)
                    {
                        var identifierMethod = typeof(GPSLocator).GetMethod(marker.identifierProperty, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                        if (identifierMethod != null)
                        {
                            identifierValue = identifierMethod.Invoke(__instance, null);
                        }
                        else
                        {
                            RLog.Error($"Could not find {marker.identifierProperty} method on GPSLocator for {marker.gameObjectName}.");
                            continue;
                        }
                    }
                    else
                    {
                        var identifierProperty = typeof(GPSLocator).GetProperty(marker.identifierProperty, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (identifierProperty != null)
                        {
                            identifierValue = identifierProperty.GetValue(__instance);
                        }
                        else
                        {
                            RLog.Error($"Could not find {marker.identifierProperty} property on GPSLocator for {marker.gameObjectName}.");
                            continue;
                        }
                    }

                    // Compare the identifier value (handle different types)
                    bool matches = false;
                    if (identifierValue is float floatValue && marker.identifierValue is float floatTarget)
                    {
                        matches = Mathf.Approximately(floatValue, floatTarget);
                    }
                    else if (identifierValue is Vector3 vectorValue && marker.identifierValue is Vector3 vectorTarget)
                    {
                        matches = Vector3.Distance(vectorValue, vectorTarget) < 0.1f;
                    }

                    if (matches)
                    {
                        // Disable the marker by setting _usePlayerAreaMask to true
                        var usePlayerAreaMaskProperty = typeof(GPSLocator).GetProperty("_usePlayerAreaMask", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (usePlayerAreaMaskProperty != null)
                        {
                            usePlayerAreaMaskProperty.SetValue(__instance, true);
                            RLog.Msg($"Disabled {__instance.gameObject.name} icon on GPS (matched {marker.identifierProperty} = {marker.identifierValue}).");
                            _processedMarkers.Add($"{marker.gameObjectName}_{marker.identifierProperty}_{marker.identifierValue}");
                            _disabledMarkerCount++;

                            // Check if we've disabled all target markers
                            if (_disabledMarkerCount >= _markersToDisable.Count)
                            {
                                RLog.Msg("All target markers have been disabled.");
                                _hasDisabledIcons = true;
                            }
                        }
                        else
                        {
                            RLog.Error($"Could not find _usePlayerAreaMask property on GPSLocator for {__instance.gameObject.name}.");
                        }
                        break; // Exit loop once found
                    }
                }
            }
        }
    }
}