using SonsSdk;
using RedLoader; // For logging messages with RLog
using HarmonyLib; // For Harmony patching (we’ll use this in the next step)
using UnityEngine; // For Unity types like Vector3 (used for GPSLocatorPickup positions)
using System.Collections.Generic; // For List and Dictionary (we’ll use Dictionary later)
using Sons.Gameplay.GPS; // For the GPSLocator component (used to control GPS markers in SOTF)

namespace BlankGPS;

public class BlankGPS : SonsMod
{
    // Step 1: Define a list to store the markers we want to disable
    // This list will hold the markers we want to target (e.g., CaveAEntranceGPS)
    // Each entry is a tuple with the GameObject name, the property to check (e.g., "_iconScale"),
    // the expected value of that property (e.g., 1.1f), and whether the property is a method (e.g., true for "Position")
    private static List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)> _defaultMarkers;

    // Step 1.1: Provide a public property to access the marker list
    // This allows other classes (e.g., GPSLocatorAwakePatch) to read the list while keeping _defaultMarkers private
    public static List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)> DefaultMarkers => _defaultMarkers;

    public BlankGPS()
    {
        RLog.Msg("BlankGPS mod loaded successfully");

        // Step 2: Initialize the marker list with all target markers
        // This includes all cave entrances and specific GPSLocatorPickup markers
        // Each tuple contains: name (e.g., "CaveAEntranceGPS"), property to check (e.g., "_iconScale" or "Position"),
        // expected value (e.g., 1.1f or a Vector3), and whether it’s a method (true for "Position", false for "_iconScale")
        _defaultMarkers = new List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)>
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

        // Step 3: Log the number of markers to confirm the list is initialized
        RLog.Msg($"Initialized {_defaultMarkers.Count} markers to disable");

        // Uncomment any of these if you need a method to run on a specific update loop.
        //OnUpdateCallback = MyUpdateMethod;
        //OnLateUpdateCallback = MyLateUpdateMethod;
        //OnFixedUpdateCallback = MyFixedUpdateMethod;
        //OnGUICallback = MyGUIMethod;

        // Step 4: Enable Harmony patching for our mod
        // This tells RedLoader to apply all Harmony patches defined in our assembly (e.g., GPSLocatorAwakePatch)
        HarmonyPatchAll = true;
    }

    protected override void OnInitializeMod()
    {
        // Do your early mod initialization which doesn't involve game or sdk references here
        Config.Init();
    }

    protected override void OnSdkInitialized()
    {
        // Do your mod initialization which involves game or sdk references here
        // This is for stuff like UI creation, event registration etc.
        BlankGPSUi.Create();

        // Add in-game settings ui for your mod.
        // SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected override void OnGameStart()
    {
        // This is called once the player spawns in the world and gains control.
    }
}

// Step 5: Harmony patch for GPSLocator.OnEnable
// This patch runs custom code whenever a GPSLocator component is enabled in the game
// GPSLocator components control GPS markers (e.g., CaveAEntranceGPS), and OnEnable is called when the marker is loaded
[HarmonyPatch(typeof(GPSLocator), "OnEnable")]
public class GPSLocatorAwakePatch
{
    // Step 6: Define a Postfix method to run after OnEnable
    // A Postfix runs after the original OnEnable method, and __instance is the specific GPSLocator component instance
    [HarmonyPostfix]
    public static void Postfix(GPSLocator __instance)
    {
        // Step 7: Safety check for __instance
        // Ensure the GPSLocator instance and its GameObject are valid before proceeding
        if (__instance == null || __instance.gameObject == null)
        {
            RLog.Error("Invalid GPSLocator instance or GameObject in OnEnable patch");
            return;
        }

        // Step 8: Find all markers in our list that match the GameObject name
        // We use LINQ’s Where to get all marker tuples with a matching name
        // This ensures we check all entries, not just the first one (e.g., for GPSLocatorPickup with different positions)
        var matchingMarkers = BlankGPS.DefaultMarkers.Where(marker => marker.gameObjectName == __instance.gameObject.name);
        if (!matchingMarkers.Any()) return;

        // Step 9: Iterate over all matching markers to find the correct one
        // We need to check the identifier property (e.g., _iconScale or Position) for each match
        foreach (var matchingMarker in matchingMarkers)
        {
            // Get the identifier property value (e.g., _iconScale or Position) from the GPSLocator
            // If it’s a property (e.g., _iconScale), use AccessTools.Property; if it’s a method (e.g., Position), use AccessTools.Method
            object identifierValue = null;
            if (!matchingMarker.isMethod)
            {
                identifierValue = AccessTools.Property(typeof(GPSLocator), "_iconScale")?.GetValue(__instance);
                if (identifierValue == null)
                {
                    RLog.Error($"Could not find property _iconScale on GPSLocator for {matchingMarker.gameObjectName}");
                    return;
                }
            }
            else
            {
                // Handle methods like Position for GPSLocatorPickup
                identifierValue = AccessTools.Method(typeof(GPSLocator), matchingMarker.identifierProperty)?.Invoke(__instance, null);
                if (identifierValue == null)
                {
                    RLog.Error($"Could not find method {matchingMarker.identifierProperty} on GPSLocator for {matchingMarker.gameObjectName}");
                    return;
                }
            }

            // Step 10: Compare the identifier value to the expected value
            // This ensures we’re targeting the correct GPSLocator (e.g., _iconScale == 1.1f or matching Position)
            bool matches = false;
            if (identifierValue is float floatValue && matchingMarker.identifierValue is float floatTarget)
            {
                matches = Mathf.Approximately(floatValue, floatTarget);
            }
            else if (identifierValue is Vector3 vectorValue && matchingMarker.identifierValue is Vector3 vectorTarget)
            {
                matches = Vector3.Distance(vectorValue, vectorTarget) < 0.1f;
            }

            if (matches)
            {
                // Step 11: Log a message if we found a target marker with the correct properties
                // This confirms that we’ve detected the specific marker we want to disable
                RLog.Msg($"Found target marker: {__instance.gameObject.name} with {matchingMarker.identifierProperty} = {identifierValue}");

                // Step 12: Disable the marker by calling Enable(false)
                // This explicitly disables the GPSLocator, hiding the marker on the GPS
                __instance.Enable(false);

                // Step 13: Log a message to confirm the marker is disabled
                // This helps us verify that the marker was successfully disabled
                RLog.Msg($"Disabled marker: {__instance.gameObject.name}");

                // Break after disabling the marker, as we’ve found the correct match
                break;
            }
        }
    }
}