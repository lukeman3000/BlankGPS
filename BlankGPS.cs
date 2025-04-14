using SonsSdk;
using RedLoader; // For logging messages with RLog
using HarmonyLib; // For Harmony patching (we’ll use this in the next step)
using UnityEngine; // For Unity types like Vector3 (used for GPSLocatorPickup positions)
using System.Collections.Generic; // For List and Dictionary (we’ll use Dictionary later)
using System.IO; // For File and Path operations
using Alt.Json; // For JSON serialization/deserialization
using Sons.Gameplay.GPS; // For the GPSLocator component (used to control GPS markers in SOTF)

namespace BlankGPS;

// Step 5.1: Define a class to store state for each GPSLocator
// This will be stored directly in the Markers dictionary without attaching to a GameObject
public class GPSLocatorState
{
    // The GPSLocator component instance
    public GPSLocator Locator { get; set; }

    // Tracks whether the marker is currently disabled (true = disabled, false = enabled)
    public bool IsDisabled { get; set; }
}

// Step 1.3: Minimal class for JSON testing (only marker names)
public class MinimalMarkerData
{
    public string GameObjectName { get; set; }
}

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

    // Step 5.2: Define a Dictionary to store managed GPSLocator instances
    // The key is the GameObject name, and the value is the GPSLocatorState object
    // This allows us to reference markers later without searching _defaultMarkers again
    private static Dictionary<string, GPSLocatorState> _markers = new Dictionary<string, GPSLocatorState>();

    // Step 5.3: Provide a public property to access the managed markers
    // This allows other classes (e.g., for proximity enabling) to read the dictionary
    public static Dictionary<string, GPSLocatorState> Markers => _markers;

    public BlankGPS()
    {
        RLog.Msg("=== BlankGPS Constructor Started ===");
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

        // Step 2.1: Load the minimal marker list from a JSON file in the mod folder's BlankGPS subfolder
        RLog.Msg("Getting mods folder path...");
        string modsFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        RLog.Msg($"Mods folder: {modsFolder}");

        RLog.Msg("Constructing subfolder path...");
        string subFolder = Path.Combine(modsFolder, "BlankGPS");
        RLog.Msg($"Subfolder: {subFolder}");

        RLog.Msg("Constructing config file path...");
        string configPath = Path.Combine(subFolder, "BlankGPS.json");
        RLog.Msg($"Config path: {configPath}");

        try
        {
            RLog.Msg("Checking if subfolder exists...");
            // Create the BlankGPS subfolder if it doesn't exist
            if (!Directory.Exists(subFolder))
            {
                RLog.Msg("Creating subfolder...");
                Directory.CreateDirectory(subFolder);
                RLog.Msg($"Created BlankGPS subfolder at {subFolder}.");
            }
            else
            {
                RLog.Msg("Subfolder already exists.");
            }

            RLog.Msg("Checking if config file exists...");
            List<MinimalMarkerData> markerDataList;

            if (!File.Exists(configPath))
            {
                RLog.Msg("Config file does not exist. Creating...");
                // Create the JSON file with the full list of marker names
                markerDataList = _defaultMarkers.Select(m => new MinimalMarkerData { GameObjectName = m.gameObjectName }).ToList();
                RLog.Msg("Serializing marker data...");
                string defaultJson = Alt.Json.JsonConvert.SerializeObject(markerDataList, Formatting.Indented);
                RLog.Msg("Writing JSON to file...");
                File.WriteAllText(configPath, defaultJson);
                RLog.Msg($"Created minimal configuration file at {configPath}.");
            }
            else
            {
                RLog.Msg("Config file exists. Reading JSON...");
                // Load the JSON file
                string json = File.ReadAllText(configPath);
                RLog.Msg("Deserializing JSON...");
                markerDataList = Alt.Json.JsonConvert.DeserializeObject<List<MinimalMarkerData>>(json);
                if (markerDataList == null)
                {
                    RLog.Error($"Failed to deserialize BlankGPS.json at {configPath}. Using default marker names.");
                    markerDataList = _defaultMarkers.Select(m => new MinimalMarkerData { GameObjectName = m.gameObjectName }).ToList();
                }
                else
                {
                    RLog.Msg($"Loaded minimal marker list from {configPath} with {markerDataList.Count} markers.");
                }
            }

            // Log the loaded marker names for verification
            foreach (var marker in markerDataList)
            {
                RLog.Msg($"Loaded marker: {marker.GameObjectName}");
            }
        }
        catch (System.Exception e)
        {
            RLog.Error($"Failed to load or create minimal configuration file at {configPath}: {e.Message}. Continuing with default marker list.");
        }

        // Step 3: Log the number of markers to confirm the list is initialized
        RLog.Msg($"Initialized {_defaultMarkers.Count} markers to disable");

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

                // Step 14: Add the GPSLocator to the dictionary of managed markers
                // Create a GPSLocatorState object and store it in the dictionary
                GPSLocatorState state = new GPSLocatorState();
                state.Locator = __instance;
                state.IsDisabled = true;

                // Use the GameObject name as the key; for GPSLocatorPickup, append the position to make it unique
                string key = matchingMarker.isMethod ? $"{__instance.gameObject.name}_{identifierValue}" : __instance.gameObject.name;
                BlankGPS.Markers[key] = state;
                RLog.Msg($"Added marker to dictionary: {key}");

                // Break after disabling the marker, as we’ve found the correct match
                break;
            }
        }
    }
}