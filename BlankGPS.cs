using SonsSdk;
using RedLoader; // For logging messages with RLog
using HarmonyLib; // For Harmony patching
using UnityEngine; // For Unity types like Vector3 (used for GPSLocatorPickup positions)
using System.Collections.Generic; // For List and Dictionary
using Sons.Gameplay.GPS; // For the GPSLocator component (used to control GPS markers in SOTF)

namespace BlankGPS;

// Step 1: Define a class to store state for each GPSLocator
// This will be stored directly in the Markers dictionary without attaching to a GameObject
public class GPSLocatorState
{
    // The GPSLocator component instance
    public GPSLocator Locator { get; set; }

    // Tracks whether the marker is currently disabled (true = disabled, false = enabled)
    public bool IsDisabled { get; set; }
}

// Step 2: Component to handle proximity trigger events
[RegisterTypeInIl2Cpp]
public class ProximityTrigger : MonoBehaviour
{
    private bool _hasTriggered = false;
    private bool _hasExited = false;
    private GPSLocator _gpsLocator;
    private int _playerLayer;

    private void Start()
    {
        // Step 2.1: Get the GPSLocator component from the parent GameObject
        _gpsLocator = transform.parent.GetComponent<GPSLocator>();
        if (_gpsLocator == null)
        {
            RLog.Error($"Could not find GPSLocator on parent GameObject: {transform.parent.name}");
        }

        // Step 2.2: Set the GameObject to the player's layer to match LocalPlayer
        _playerLayer = LayerMask.NameToLayer("Player");
        if (_playerLayer == -1)
        {
            RLog.Error("Could not find Player layer! Using default layer.");
        }
        else
        {
            gameObject.layer = _playerLayer;
        }
    }

    // Step 2.3: Method to check if the layer was set to Player
    public bool IsLayerSetToPlayer()
    {
        return _playerLayer != -1;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Step 2.4: Check if the collider is the player (tagged "Player" and on the Player layer)
        if (other.CompareTag("Player") && other.gameObject.layer == _playerLayer && !_hasTriggered)
        {
            _hasTriggered = true;
            _hasExited = false; // Reset the exit flag when entering
            RLog.Msg($"Player entered proximity trigger at position {transform.position}!");

            // Step 2.5: Enable the marker using BlankGPS's marker management
            if (_gpsLocator != null)
            {
                // Find the marker's state in the Markers dictionary
                string key = _gpsLocator.gameObject.name;
                if (!BlankGPS.Markers.ContainsKey(key))
                {
                    // For GPSLocatorPickup, the key includes the position
                    foreach (var marker in BlankGPS.Markers)
                    {
                        if (marker.Key.StartsWith(_gpsLocator.gameObject.name))
                        {
                            key = marker.Key;
                            break;
                        }
                    }
                }

                if (BlankGPS.Markers.TryGetValue(key, out GPSLocatorState state))
                {
                    // Use MarkerEnable to enable the marker with the correct icon scale
                    BlankGPS.MarkerEnable(_gpsLocator, BlankGPS.DefaultMarkers.Find(m => m.gameObjectName == _gpsLocator.gameObject.name).iconScale);

                    // Update the state to reflect that the marker is enabled
                    state.IsDisabled = false;

                    RLog.Msg($"Enabled marker: {_gpsLocator.gameObject.name}");
                }
                else
                {
                    RLog.Error($"Could not find marker state for {_gpsLocator.gameObject.name} in Markers dictionary!");
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Step 2.6: Reset the flag when the player leaves the trigger area (tagged "Player" and on the Player layer)
        if (other.CompareTag("Player") && other.gameObject.layer == _playerLayer && !_hasExited)
        {
            _hasExited = true;
            _hasTriggered = false; // Reset the entry flag when exiting
            RLog.Msg($"Player exited proximity trigger at position {transform.position}");
        }
    }
}

public class BlankGPS : SonsMod
{
    // Step 3: Define a list to store the markers we want to disable
    // List of markers to target, each with a name, icon scale, and position
    private static List<(string gameObjectName, float iconScale, Vector3 position)> _defaultMarkers;

    // Step 4: Provide a public property to access the marker list
    // This allows other classes (e.g., GPSLocatorAwakePatch) to read the list while keeping _defaultMarkers private
    public static List<(string gameObjectName, float iconScale, Vector3 position)> DefaultMarkers => _defaultMarkers;

    // Step 5: Define a Dictionary to store managed GPSLocator instances
    // The key is the GameObject name, and the value is the GPSLocatorState object
    // This allows us to reference markers later without searching _defaultMarkers again
    private static Dictionary<string, GPSLocatorState> _markers = new Dictionary<string, GPSLocatorState>();

    // Step 6: Provide a public property to access the managed markers
    // This allows other classes (e.g., for proximity enabling) to read the dictionary
    public static Dictionary<string, GPSLocatorState> Markers => _markers;

    // Step 7: Enables a marker by setting its icon scale to the original value and refreshing the GPS
    public static void MarkerEnable(GPSLocator locator, float iconScale)
    {
        // Set the icon scale to the original value
        AccessTools.Property(typeof(GPSLocator), "_iconScale")?.SetValue(locator, iconScale);

        // Refresh the GPS to apply the change
        AccessTools.Method(typeof(GPSLocator), "ForceRefresh")?.Invoke(locator, null);
    }

    // Step 8: Disables a marker by setting its icon scale to 0 and refreshing the GPS
    public static bool MarkerDisable(GPSLocator locator)
    {
        try
        {
            // Set the icon scale to 0 to hide the marker
            AccessTools.Property(typeof(GPSLocator), "_iconScale")?.SetValue(locator, 0f);

            // Refresh the GPS to apply the change
            AccessTools.Method(typeof(GPSLocator), "ForceRefresh")?.Invoke(locator, null);
            return true; // Success
        }
        catch (System.Exception e)
        {
            RLog.Error($"Failed to disable marker {locator.gameObject.name}: {e.Message}");
            return false; // Failure
        }
    }

    // Step 9: Helper method to create and configure a ProximityTrigger for a marker
    public static (bool success, bool layerSet) CreateProximityTrigger(GameObject gpsObject)
    {
        // Step 9.1: Create a child GameObject for the proximity trigger
        GameObject triggerObject = new GameObject($"ProximityTrigger_{gpsObject.name}");

        // Step 9.2: Make triggerObject a child of gpsObject without preserving world position
        triggerObject.transform.SetParent(gpsObject.transform, false);

        // Step 9.3: Add a SphereCollider to triggerObject
        SphereCollider collider = triggerObject.AddComponent<SphereCollider>();
        if (collider == null)
        {
            RLog.Error($"Failed to add SphereCollider to {triggerObject.name}!");
            return (false, false);
        }

        // Step 9.4: Configure the SphereCollider
        collider.radius = 10f;
        collider.isTrigger = true;

        // Step 9.5: Attach the ProximityTrigger component to triggerObject
        ProximityTrigger trigger = triggerObject.AddComponent<ProximityTrigger>();
        if (trigger == null)
        {
            RLog.Error($"Failed to add ProximityTrigger to {triggerObject.name}!");
            return (false, false);
        }

        // Step 9.6: Check if the layer was set to Player
        bool layerSet = trigger.IsLayerSetToPlayer();
        return (true, layerSet);
    }

    public BlankGPS()
    {
        // Step 10: Initialize the marker list with all target markers
        // Each marker has a name, icon scale, and position
        _defaultMarkers = new List<(string gameObjectName, float iconScale, Vector3 position)>
        {
            ("CaveAEntranceGPS", 1.1f, new Vector3(-422.92f, 14.97f, 1515.22f)),
            ("CaveBEntranceGPS", 1.1f, new Vector3(-1108.61f, 128.04f, -175.92f)),
            ("CaveCEntranceGPS", 1.1f, new Vector3(-531.23f, 196.68f, 128.6799f)),
            ("CaveDAEntranceGPS", 1.1f, new Vector3(-534.868f, 291.38f, -630.3311f)),
            ("CaveDBEntranceGPS", 1.1f, new Vector3(972.29f, 246.1f, -407.12f)),
            ("CaveFEntranceGPS", 1.1f, new Vector3(1288.067f, 179.286f, 588.5831f)),
            ("CaveGAEntranceGPS", 1.1f, new Vector3(1694.611f, 28.5672f, 1042.029f)),
            ("SnowCaveAEntranceGPS", 1.1f, new Vector3(-137.06f, 408.01f, -92.55f)),
            ("SnowCaveBEntranceGPS", 1.1f, new Vector3(430.54f, 516.5f, -206.81f)),
            ("SnowCaveCEntranceGPS", 1.1f, new Vector3(-106.84f, 396.46f, -1058.57f)),
            ("GPSLocatorPickup", 0.7f, new Vector3(-626.3061f, 145.3385f, 386.1634f)),
            ("GPSLocatorPickup", 0.7f, new Vector3(-1340.964f, 95.4219f, 1411.981f)),
            ("GPSLocatorPickup", 0.7f, new Vector3(-1797.652f, 14.4886f, 577.0323f)),
            ("BunkerAEntranceGPS", 0.8f, new Vector3(-477.83f, 86.9f, 710.4299f)),
            ("BunkerBEntranceGPS", 0.8f, new Vector3(-1133.19f, 278.63f, -1101.63f)),
            ("BunkerCEntranceGPS", 0.8f, new Vector3(1109.243f, 128.325f, 1007.23f)),
            ("BunkerEntertainmentEntranceGPS", 0.8f, new Vector3(-1189.18f, 67.94f, 129.62f)),
            ("BunkerFoodEntranceGPS", 0.8f, new Vector3(-1014.03f, 99.79f, 1029.67f)),
            ("BunkerLuxuryEntranceGPS", 0.8f, new Vector3(1750.42f, 41.27f, 552.599f)),
            ("BunkerResidentialEntranceGPS", 0.8f, new Vector3(1233.412f, 238.91f, -654.541f))
        };

        // Step 11: Log the number of markers to confirm the list is initialized
        RLog.Msg($"Initialized {_defaultMarkers.Count} markers to disable");

        // Step 12: Enable Harmony patching for our mod
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
        // Step 13: Add ProximityTrigger to all markers in the Markers dictionary
        int triggerCount = 0;
        int layerSetCount = 0;
        int playerLayer = LayerMask.NameToLayer("Player");

        foreach (var marker in Markers)
        {
            var (success, layerSet) = CreateProximityTrigger(marker.Value.Locator.gameObject);
            if (success)
            {
                triggerCount++;
                if (layerSet)
                {
                    layerSetCount++;
                }
            }
        }

        // Step 14: Log the summary of added triggers and layer assignment
        RLog.Msg($"Added {triggerCount} ProximityTriggers with SphereColliders for targeted GPSLocators (set to Player layer ID: {playerLayer})");
    }
}

// Step 15: Harmony patch for GPSLocator.OnEnable
// This patch runs custom code whenever a GPSLocator component is enabled in the game
// GPSLocator components control GPS markers (e.g., CaveAEntranceGPS), and OnEnable is called when the marker is loaded
[HarmonyPatch(typeof(GPSLocator), "OnEnable")]
public class GPSLocatorAwakePatch
{
    // Step 16: Track the number of markers disabled
    private static int _disabledCount = 0;

    [HarmonyPostfix]
    public static void Postfix(GPSLocator __instance)
    {
        // Step 17: Safety check for __instance
        // Ensure the GPSLocator instance and its GameObject are valid before proceeding
        if (__instance == null || __instance.gameObject == null)
        {
            RLog.Error("Invalid GPSLocator instance or GameObject in OnEnable patch");
            return;
        }

        // Step 18: Find all markers in our list that match the GameObject name
        // We use LINQ’s Where to get all marker tuples with a matching name
        // This ensures we check all entries, not just the ones for GPSLocatorPickup with different positions
        var matchingMarkers = BlankGPS.DefaultMarkers.Where(marker => marker.gameObjectName == __instance.gameObject.name);
        if (!matchingMarkers.Any()) return;

        // Step 19: Iterate over all matching markers to find the correct one
        foreach (var matchingMarker in matchingMarkers)
        {
            bool matches = false;

            // For Cave and Bunker markers, check the _maxVisualRange property
            if (matchingMarker.gameObjectName.Contains("Cave") || matchingMarker.gameObjectName.Contains("Bunker"))
            {
                // Fetch _maxVisualRange property
                object maxVisualRangeValue = AccessTools.Property(typeof(GPSLocator), "_maxVisualRange")?.GetValue(__instance);
                if (maxVisualRangeValue == null)
                {
                    RLog.Error($"Could not find property _maxVisualRange on GPSLocator for {matchingMarker.gameObjectName}");
                    return;
                }

                // Compare _maxVisualRange with the expected value (600)
                if (maxVisualRangeValue is int intValue)
                {
                    matches = intValue == 600;
                }
                else
                {
                    RLog.Error($"_maxVisualRange for {matchingMarker.gameObjectName} is not an int: {maxVisualRangeValue?.GetType().Name}");
                    return;
                }
            }
            // For GPSLocatorPickup markers, check the Position method
            else if (matchingMarker.gameObjectName == "GPSLocatorPickup")
            {
                // Get the Position method result from the GPSLocator
                object positionValue = AccessTools.Method(typeof(GPSLocator), "Position")?.Invoke(__instance, null);
                if (positionValue == null)
                {
                    RLog.Error($"Could not find method Position on GPSLocator for {matchingMarker.gameObjectName}");
                    return;
                }

                // Compare the fetched position with the stored position
                if (positionValue is Vector3 vectorValue)
                {
                    matches = Vector3.Distance(vectorValue, matchingMarker.position) < 0.1f;
                }
            }

            if (matches)
            {
                // Step 20: Disable the marker and increment the counter if successful
                if (BlankGPS.MarkerDisable(__instance))
                {
                    _disabledCount++;
                }

                // Step 21: Add the GPSLocator to the dictionary of managed markers
                // Create a GPSLocatorState object and store it in the dictionary
                GPSLocatorState state = new GPSLocatorState();
                state.Locator = __instance;
                state.IsDisabled = true;

                // Use the GameObject name as the key; for GPSLocatorPickup, append the position to make it unique
                string key = matchingMarker.gameObjectName == "GPSLocatorPickup" ? $"{__instance.gameObject.name}_{matchingMarker.position}" : __instance.gameObject.name;
                BlankGPS.Markers[key] = state;

                // Break after processing the marker, as we’ve found the correct match
                break;
            }
        }

        // Step 22: Log the total number of markers disabled after all markers are processed
        if (_disabledCount == BlankGPS.DefaultMarkers.Count || BlankGPS.Markers.Count == BlankGPS.DefaultMarkers.Count)
        {
            RLog.Msg($"Disabled {_disabledCount} markers for targeted GPSLocators");
            _disabledCount = 0; // Reset for future loads
        }
    }
}