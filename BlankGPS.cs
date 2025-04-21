using SonsSdk;
using RedLoader; // For logging messages with RLog
using HarmonyLib; // For Harmony patching
using UnityEngine; // For Unity types like Vector3 (used for GPSLocatorPickup positions)
using System.Collections.Generic; // For List and Dictionary
using Sons.Gameplay.GPS; // For the GPSLocator component (used to control GPS markers in SOTF)
using SUI; // For SettingsRegistry

namespace BlankGPS;

// Step 1: Define a class to store state for each GPSLocator
// This will be stored directly in the Markers dictionary without attaching to a GameObject
public class GPSLocatorState
{
    // The GPSLocator component instance
    public GPSLocator Locator { get; set; }

    // Tracks whether the marker is currently disabled (true = disabled, false = enabled)
    public bool IsDisabled { get; set; }

    // Stores the original icon scale for re-enabling the marker
    public float OriginalIconScale { get; set; }

    // Stores the reference to the ProximityTrigger GameObject (if created)
    public GameObject TriggerObject { get; set; }
}

// Step 2: Component to handle proximity trigger events
[RegisterTypeInIl2Cpp]
public class ProximityTrigger : MonoBehaviour
{
    private bool _hasTriggered = false;
    private bool _hasExited = false;
    private GPSLocator _gpsLocator;
    private int _playerLayer;
    private string _markerKey; // Cached key for the marker in BlankGPS.Markers

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
        gameObject.layer = _playerLayer;

        // Step 2.3: Cache the marker key for this GPSLocator
        if (_gpsLocator != null)
        {
            _markerKey = _gpsLocator.gameObject.name;
            if (!BlankGPS.Markers.ContainsKey(_markerKey))
            {
                // For GPSLocatorPickup, the key includes the position
                foreach (var marker in BlankGPS.Markers)
                {
                    if (marker.Key.StartsWith(_gpsLocator.gameObject.name))
                    {
                        _markerKey = marker.Key;
                        break;
                    }
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Step 2.4: Check if the collider is the player (tagged "Player" and on the Player layer)
        if (other.CompareTag("Player") && other.gameObject.layer == _playerLayer && !_hasTriggered)
        {
            _hasTriggered = true;
            _hasExited = false; // Reset the exit flag when entering
            RLog.Msg($"Player entered proximity trigger at position {transform.position}!"); // Reintroduced for testing

            // Step 2.5: Enable the marker if proximity is enabled and the marker type is managed
            if (_gpsLocator != null && Config.ProximityEnabled.Value)
            {
                if (BlankGPS.Markers.TryGetValue(_markerKey, out GPSLocatorState state))
                {
                    // Check if the marker type is managed based on config settings
                    bool isTypeManaged = false;
                    if (_markerKey.Contains("Cave") && Config.ManageCaves.Value)
                    {
                        isTypeManaged = true;
                    }
                    else if (_markerKey.Contains("GPSLocatorPickup") && Config.ManageTeamB.Value)
                    {
                        isTypeManaged = true;
                    }
                    else if (_markerKey.Contains("Bunker") && Config.ManageBunkers.Value)
                    {
                        isTypeManaged = true;
                    }

                    // Only re-enable the marker if its type is managed and ProximityEnabled is true
                    if (isTypeManaged)
                    {
                        BlankGPS.MarkerEnable(_gpsLocator, state.OriginalIconScale);
                        state.IsDisabled = false;
                        RLog.Msg($"Enabled marker: {_gpsLocator.gameObject.name}");
                    }
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
            RLog.Msg($"Player exited proximity trigger at position {transform.position}"); // Reintroduced for testing
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
        locator._iconScale = iconScale;

        // Refresh the GPS to apply the change
        locator.ForceRefresh();
    }

    // Step 8: Disables a marker by setting its icon scale to 0 and refreshing the GPS
    public static void MarkerDisable(GPSLocator locator)
    {
        // Set the icon scale to 0 to hide the marker
        locator._iconScale = 0f;

        // Refresh the GPS to apply the change
        locator.ForceRefresh();
    }

    // Step 9: Updates the state of all markers of a specific type based on the manage setting
    public static void UpdateMarkerStatesForType(string typeIdentifier, bool shouldManage)
    {
        int affectedCount = 0;
        int triggerCount = 0;
        foreach (var marker in Markers)
        {
            string markerName = marker.Key;
            GPSLocatorState state = marker.Value;

            // Determine if the marker matches the type
            bool isMatchingType = false;
            if (typeIdentifier == "Cave" && markerName.Contains("Cave"))
            {
                isMatchingType = true;
            }
            else if (typeIdentifier == "GPSLocatorPickup" && markerName.Contains("GPSLocatorPickup"))
            {
                isMatchingType = true;
            }
            else if (typeIdentifier == "Bunker" && markerName.Contains("Bunker"))
            {
                isMatchingType = true;
            }

            if (isMatchingType)
            {
                if (shouldManage)
                {
                    // Disable the marker if its type should now be managed
                    MarkerDisable(state.Locator);
                    state.IsDisabled = true;
                    affectedCount++;

                    // Create a trigger and collider for the marker if it doesn't already have one
                    if (state.TriggerObject == null)
                    {
                        state.TriggerObject = CreateProximityTrigger(state.Locator.gameObject);
                        if (state.TriggerObject != null)
                        {
                            triggerCount++;
                        }
                    }
                }
                else
                {
                    // Re-enable the marker if its type should no longer be managed
                    MarkerEnable(state.Locator, state.OriginalIconScale);
                    state.IsDisabled = false;
                    affectedCount++;

                    // Destroy the trigger and collider if they exist
                    if (state.TriggerObject != null)
                    {
                        UnityEngine.Object.Destroy(state.TriggerObject);
                        state.TriggerObject = null;
                    }
                }
            }
        }

        // Log a summary of the marker state changes
        if (affectedCount > 0)
        {
            string action = shouldManage ? "Disabled" : "Enabled";
            string typeName = typeIdentifier == "Cave" ? "cave" : typeIdentifier == "GPSLocatorPickup" ? "Team B" : "bunker";
            RLog.Msg($"{action} {affectedCount} {typeName} markers due to config change");
        }

        // Log a summary of triggers created
        if (triggerCount > 0)
        {
            string typeName = typeIdentifier == "Cave" ? "cave" : typeIdentifier == "GPSLocatorPickup" ? "Team B" : "bunker";
            RLog.Msg($"Added {triggerCount} ProximityTriggers with SphereColliders for {typeName} markers");
        }
    }

    public static GameObject CreateProximityTrigger(GameObject gpsObject)
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
            return null;
        }

        // Step 9.4: Configure the SphereCollider
        collider.radius = Config.ProximityRadius.Value;
        collider.isTrigger = true;

        // Step 9.5: Attach the ProximityTrigger component to triggerObject
        ProximityTrigger trigger = triggerObject.AddComponent<ProximityTrigger>();
        if (trigger == null)
        {
            RLog.Error($"Failed to add ProximityTrigger to {triggerObject.name}!");
            UnityEngine.Object.Destroy(triggerObject);
            return null;
        }

        return triggerObject;
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
        // Step 12.1: Initialize configuration settings
        Config.Init();
    }

    protected override void OnSdkInitialized()
    {
        // Step 12.2: Create the in-game settings UI and initialize the BlankGPS UI
        BlankGPSUi.Create();
        SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected override void OnGameStart()
    {
        // This is called once the player spawns in the world and gains control.
        // Step 13: Add ProximityTrigger to managed markers in the Markers dictionary
        int triggerCount = 0;
        int disabledCount = 0;
        int playerLayer = LayerMask.NameToLayer("Player");

        foreach (var marker in Markers)
        {
            string markerName = marker.Key;
            GPSLocatorState state = marker.Value;

            // Check if the marker type is managed based on config settings
            bool isTypeManaged = false;
            if (markerName.Contains("Cave") && Config.ManageCaves.Value)
            {
                isTypeManaged = true;
            }
            else if (markerName.Contains("GPSLocatorPickup") && Config.ManageTeamB.Value)
            {
                isTypeManaged = true;
            }
            else if (markerName.Contains("Bunker") && Config.ManageBunkers.Value)
            {
                isTypeManaged = true;
            }

            // Only create a trigger if the marker type is managed
            if (isTypeManaged)
            {
                GameObject triggerObject = CreateProximityTrigger(marker.Value.Locator.gameObject);
                if (triggerObject != null)
                {
                    state.TriggerObject = triggerObject;
                    triggerCount++;
                }
            }

            // Count the number of markers that were disabled at start
            if (state.IsDisabled)
            {
                disabledCount++;
            }
        }

        // Step 14: Log the summary of added triggers, disabled markers, and processed markers
        RLog.Msg($"Added {triggerCount} ProximityTriggers with SphereColliders for targeted GPSLocators (set to Player layer ID: {playerLayer})");
        RLog.Msg($"Disabled {disabledCount} markers at game start");
        RLog.Msg($"Processed {Markers.Count} out of {DefaultMarkers.Count} targeted GPSLocators");
    }
}

// Step 15: Harmony patch for GPSLocator.OnEnable
// This patch runs custom code whenever a GPSLocator component is enabled in the game
// GPSLocator components control GPS markers (e.g., CaveAEntranceGPS), and OnEnable is called when the marker is loaded
[HarmonyPatch(typeof(GPSLocator), "OnEnable")]
public class GPSLocatorAwakePatch
{
    // Step 16: Find all markers in our list that match the GameObject name
    // We use LINQ’s Where to get all marker tuples with a matching name
    // This ensures we check all entries, not just the ones for GPSLocatorPickup with different positions
    [HarmonyPostfix]
    public static void Postfix(GPSLocator __instance)
    {
        var matchingMarkers = BlankGPS.DefaultMarkers.Where(marker => marker.gameObjectName == __instance.gameObject.name);
        if (!matchingMarkers.Any()) return;

        // Step 17: Iterate over all matching markers to find the correct one
        foreach (var matchingMarker in matchingMarkers)
        {
            bool matches = false;

            // For Cave and Bunker markers, check the _maxVisualRange property
            if (matchingMarker.gameObjectName.Contains("Cave") || matchingMarker.gameObjectName.Contains("Bunker"))
            {
                // Fetch _maxVisualRange property
                int maxVisualRangeValue = __instance._maxVisualRange;
                if (maxVisualRangeValue == 0) // Check if the value is 0 (default if not set)
                {
                    RLog.Error($"Could not find property _maxVisualRange on GPSLocator for {matchingMarker.gameObjectName}");
                    return;
                }

                // Compare _maxVisualRange with the expected value (600)
                matches = maxVisualRangeValue == 600;
            }
            // For GPSLocatorPickup markers, check the Position method
            else if (matchingMarker.gameObjectName == "GPSLocatorPickup")
            {
                // Get the Position method result from the GPSLocator
                Vector3 positionValue = __instance.Position();
                if (positionValue == Vector3.zero) // Check if the value is zero (default if not set)
                {
                    RLog.Error($"Could not find method Position on GPSLocator for {matchingMarker.gameObjectName}");
                    return;
                }

                // Compare the fetched position with the stored position
                matches = Vector3.Distance(positionValue, matchingMarker.position) < 0.1f;
            }

            if (matches)
            {
                // Step 18: Determine if the marker type should be managed based on config settings
                bool shouldDisable = false;
                if (matchingMarker.gameObjectName.Contains("Cave") && Config.ManageCaves.Value)
                {
                    shouldDisable = true;
                }
                else if (matchingMarker.gameObjectName == "GPSLocatorPickup" && Config.ManageTeamB.Value)
                {
                    shouldDisable = true;
                }
                else if (matchingMarker.gameObjectName.Contains("Bunker") && Config.ManageBunkers.Value)
                {
                    shouldDisable = true;
                }

                // Step 19: Disable the marker at game start if its type is managed
                if (shouldDisable)
                {
                    BlankGPS.MarkerDisable(__instance);
                }

                // Step 20: Add the GPSLocator to the dictionary of managed markers
                // Create a GPSLocatorState object and store it in the dictionary
                GPSLocatorState state = new GPSLocatorState();
                state.Locator = __instance;
                state.IsDisabled = shouldDisable; // Reflect whether the marker is disabled
                state.OriginalIconScale = matchingMarker.iconScale; // Store the original icon scale
                state.TriggerObject = null; // Initialize with no trigger (will be set in OnGameStart if managed)

                // Use the GameObject name as the key; for GPSLocatorPickup, append the position to make it unique
                string key = matchingMarker.gameObjectName == "GPSLocatorPickup" ? $"{__instance.gameObject.name}_{matchingMarker.position}" : __instance.gameObject.name;
                BlankGPS.Markers[key] = state;

                // Break after processing the marker, as we’ve found the correct match
                break;
            }
        }
    }
}