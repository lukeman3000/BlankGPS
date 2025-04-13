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

        // Step 2: Initialize the marker list with a single marker for testing
        // We’ll start with just CaveAEntranceGPS to keep our initial tests simple
        // The tuple contains: name ("CaveAEntranceGPS"), property to check ("_iconScale"), expected value (1.1f), and whether it’s a method (false)
        _defaultMarkers = new List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)>
        {
            ("CaveAEntranceGPS", "_iconScale", 1.1f, false)
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

        // Step 8: Find the first marker in our list that matches the GameObject name
        // We use LINQ’s FirstOrDefault to get the marker tuple (if any) with a matching name
        // If no match is found, matchingMarker.gameObjectName will be null, and we return early
        var matchingMarker = BlankGPS.DefaultMarkers.FirstOrDefault(marker => marker.gameObjectName == __instance.gameObject.name);
        if (matchingMarker.gameObjectName == null) return;

        // Step 9: Get the identifier property value (e.g., _iconScale) from the GPSLocator
        // We now know _iconScale is a property, so we use AccessTools.Property
        // For now, we only handle fields or properties (not methods like Position); we’ll add that later
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
            // We’ll handle methods (e.g., Position) later when we add GPSLocatorPickup markers
            return;
        }

        // Step 10: Compare the identifier value to the expected value
        // This ensures we’re targeting the correct GPSLocator (e.g., _iconScale == 1.1f)
        bool matches = false;
        if (identifierValue is float floatValue && matchingMarker.identifierValue is float floatTarget)
        {
            matches = Mathf.Approximately(floatValue, floatTarget);
        }

        if (matches)
        {
            // Step 11: Log a message if we found a target marker with the correct properties
            // This confirms that we’ve detected the specific marker we want to disable (e.g., CaveAEntranceGPS with _iconScale = 1.1)
            RLog.Msg($"Found target marker: {__instance.gameObject.name} with {matchingMarker.identifierProperty} = {identifierValue}");

            // Step 12: Disable the marker by calling Enable(false)
            // This explicitly disables the GPSLocator, hiding the marker on the GPS
            __instance.Enable(false);

            // Step 13: Log a message to confirm the marker is disabled
            // This helps us verify that the marker was successfully disabled
            RLog.Msg($"Disabled marker: {__instance.gameObject.name}");
        }
    }
}