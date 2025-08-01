﻿using HarmonyLib;
using RedLoader;
using RedLoader.Unity.IL2CPP.Utils;
using Sons.Gameplay.GPS;
using Sons.Inventory;
using SonsSdk;
using SUI;
using System.Collections.Generic;
using System.Linq; // Added for string.Join
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace BlankGPS;

// Class to hold save data for marker states
[System.Serializable]
public class SaveData
{
    public Dictionary<string, bool> MarkerStates;

    public SaveData()
    {
        MarkerStates = new Dictionary<string, bool>();
    }
}

// Save manager for BlankGPS marker states
public class BlankGPSSaveManager : ICustomSaveable<SaveData>
{
    public string Name => "BlankGPSSaveManager";

    // Include in player save for multiplayer compatibility
    public bool IncludeInPlayerSave => true;

    public SaveData Save()
    {
        BlankGPS.CleanMarkerDictionary();

        RLog.Debug("Saving BlankGPS marker states...");
        SaveData saveData = new SaveData();
        foreach (var marker in BlankGPS.Markers)
        {
            // Save discovery state for unmanaged markers, runtime state for managed
            bool isDisabled = BlankGPS.IsMarkerTypeManaged(marker.Key)
                ? marker.Value.IsDisabled
                : (BlankGPS._originalMarkerStates.ContainsKey(marker.Key) ? BlankGPS._originalMarkerStates[marker.Key] : true);
            saveData.MarkerStates[marker.Key] = isDisabled;
            RLog.Debug($"Saved state for {marker.Key}: IsDisabled={isDisabled}");
        }
        RLog.Debug($"Saved {saveData.MarkerStates.Count} marker states");
        RLog.Debug($"_originalMarkerStates on save: {string.Join(", ", BlankGPS._originalMarkerStates.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        return saveData;
    }

    public void Load(SaveData obj)
    {
        RLog.Debug("Loading BlankGPS marker states...");
        RLog.Debug($"_originalMarkerStates before load: {string.Join(", ", BlankGPS._originalMarkerStates.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

        if (obj == null || obj.MarkerStates == null)
        {
            RLog.Warning("No marker states loaded from SaveData");
            return;
        }

        foreach (var savedState in obj.MarkerStates)
        {
            BlankGPS._loadedMarkerStates[savedState.Key] = savedState.Value;
            RLog.Debug($"Loaded state for {savedState.Key}: IsDisabled={savedState.Value}");

            // Apply saved state to Markers
            if (BlankGPS.Markers.TryGetValue(savedState.Key, out GPSLocatorState state))
            {
                state.IsDisabled = savedState.Value;

                // Always ensure GPSLocator enabled/disabled state matches management config
                if (savedState.Key.Contains("Bunker"))
                {
                    if (BlankGPS.IsMarkerTypeManaged(savedState.Key))
                        state.Locator.Enable(true);
                    else
                        state.Locator.Enable(false);
                }

                if (state.IsDisabled)
                {
                    BlankGPS.MarkerDisable(state.Locator);
                }
                else
                {
                    BlankGPS.MarkerEnable(state.Locator, state.OriginalIconScale);
                }

                RLog.Debug($"Applied saved state for {savedState.Key}: IsDisabled={state.IsDisabled}");
            }
        }
        RLog.Debug($"Loaded {obj.MarkerStates.Count} marker states");
        RLog.Debug($"Markers after load: {string.Join(", ", BlankGPS.Markers.Where(kvp => kvp.Key.Contains("Cave")).Select(kvp => $"{kvp.Key}={kvp.Value.IsDisabled}"))}");
    }
}

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
    public string _markerKey = string.Empty; // Cached key for the marker in BlankGPS.Markers, initialized to avoid null

    private void Start()
    {
        // Step 2.1: Get the GPSLocator component from the parent GameObject
        _gpsLocator = transform.parent.GetComponent<GPSLocator>();
        if (_gpsLocator == null)
        {
            RLog.Error($"Could not find GPSLocator on parent GameObject: {transform.parent.name}");
            return;
        }

        // Step 2.2: Set the GameObject to the player's layer to match LocalPlayer
        _playerLayer = LayerMask.NameToLayer("Player");
        gameObject.layer = _playerLayer;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Step 2.4: Check if the collider is the player (tagged "Player" and on the Player layer)
        if (other.CompareTag("Player") && other.gameObject.layer == _playerLayer && !_hasTriggered)
        {
            _hasTriggered = true;
            _hasExited = false; // Reset the exit flag when entering

            // Step 2.5: Discover and enable the marker if ProximityDiscovery is enabled and the marker type is managed
            if (_gpsLocator != null && !string.IsNullOrEmpty(_markerKey) && Config.ProximityDiscovery.Value)
            {
                if (BlankGPS.Markers.TryGetValue(_markerKey, out GPSLocatorState state))
                {
                    // Check if the marker type is managed based on config settings
                    if (BlankGPS.IsMarkerTypeManaged(_markerKey))
                    {
                        // Enable bunker markers explicitly for proximity discoveries
                        if (_markerKey.Contains("Bunker"))
                        {
                            _gpsLocator.Enable(true);
                        }
                        BlankGPS.MarkerEnable(_gpsLocator, state.OriginalIconScale);
                        state.IsDisabled = false;

                        // Record discovery for current session memory
                        BlankGPS._originalMarkerStates[_markerKey] = false;

                        // Immediately update beep states after discovery
                        BlankGPS.UpdateProximityBeepStates();

                        // Destroy the trigger and collider if they exist
                        if (state.TriggerObject != null)
                        {
                            UnityEngine.Object.Destroy(state.TriggerObject);
                            state.TriggerObject = null;
                        }

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

    // Stores loaded marker states for Postfix to check (Scenario 1: Load Before Postfix)
    public static Dictionary<string, bool> _loadedMarkerStates { get; private set; } = new Dictionary<string, bool>();

    // Stores original marker states to preserve discovery status when toggling management
    public static Dictionary<string, bool> _originalMarkerStates { get; private set; } = new Dictionary<string, bool>();

    // Step 7: Sets the icon scale of a marker and refreshes the GPS
    private static void SetMarkerIconScale(GPSLocator locator, float iconScale)
    {
        locator._iconScale = iconScale;
        locator.ForceRefresh();
    }

    // Step 8: Enables a marker by setting its icon scale to the original value and refreshing the GPS
    public static void MarkerEnable(GPSLocator locator, float iconScale)
    {
        SetMarkerIconScale(locator, iconScale);

        // Restore proximity beep for Team B markers when enabling them
        if (locator.gameObject.name == "GPSLocatorPickup")
        {
            locator._shouldBeepWhenInRange = true;
        }
    }

    // Step 9: Disables a marker by setting its icon scale to 0 and refreshing the GPS
    public static void MarkerDisable(GPSLocator locator)
    {
        SetMarkerIconScale(locator, 0f);
    }

    // Step 10: Helper method to determine if a marker type is managed based on config settings
    public static bool IsMarkerTypeManaged(string markerKey)
    {
        if (markerKey.Contains("Cave") && Config.ManageCaves.Value)
            return true;
        if (markerKey.Contains("GPSLocatorPickup") && Config.ManageTeamB.Value)
            return true;
        if (markerKey.Contains("Bunker") && Config.ManageBunkers.Value)
            return true;
        return false;
    }

    public static void CleanMarkerDictionary()
    {
        var keysToRemove = Markers
            .Where(kvp => kvp.Value == null || kvp.Value.Locator == null)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            Markers.Remove(key);
            RLog.Debug($"[BlankGPS] Removed stale marker dictionary entry: {key}");
        }
    }
    public static void InitializeOriginalMarkerStates()
    {
        foreach (var marker in Markers)
        {
            if (!_originalMarkerStates.ContainsKey(marker.Key))
            {
                _originalMarkerStates[marker.Key] = true; // Mark as undiscovered by default
            }
        }
    }

    // Step 11: Updates the state of all markers of a specific type based on the manage setting
    public static void UpdateMarkerStatesForType(string typeIdentifier, bool shouldManage)
    {
        CleanMarkerDictionary();
        InitializeOriginalMarkerStates();

        RLog.Debug($"UpdateMarkerStatesForType({typeIdentifier}, shouldManage={shouldManage}) called");
        //RLog.Debug($"_originalMarkerStates before update: {string.Join(", ", _originalMarkerStates.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        //RLog.Debug($"Markers before update: {string.Join(", ", Markers.Where(kvp => kvp.Key.Contains("Cave")).Select(kvp => $"{kvp.Key}={kvp.Value.IsDisabled}"))}");

        // Step 11.1: Iterate over markers and update their state and triggers
        int affectedCount = 0;
        int triggerCount = 0;
        foreach (var marker in Markers)
        {
            GPSLocatorState state = marker.Value;

            // Step 11.2: Determine if the marker matches the type
            bool isMatchingType = false;
            if (typeIdentifier == "Cave" && marker.Key.Contains("Cave"))
            {
                isMatchingType = true;
            }
            else if (typeIdentifier == "GPSLocatorPickup" && marker.Key.Contains("GPSLocatorPickup"))
            {
                isMatchingType = true;
            }
            else if (typeIdentifier == "Bunker" && marker.Key.Contains("Bunker"))
            {
                isMatchingType = true;
            }

            if (isMatchingType)
            {
                if (shouldManage)
                {
                    // Step 11.3: Restore saved state if available, else original or default disabled
                    bool savedIsDisabled = BlankGPS._loadedMarkerStates.ContainsKey(marker.Key) ? BlankGPS._loadedMarkerStates[marker.Key] : (_originalMarkerStates.ContainsKey(marker.Key) ? _originalMarkerStates[marker.Key] : true);
                    state.IsDisabled = savedIsDisabled;
                    RLog.Debug($"Toggle {typeIdentifier} ON: Set {marker.Key} IsDisabled={state.IsDisabled}");

                    if (state.IsDisabled)
                    {
                        MarkerDisable(state.Locator);
                        affectedCount++;
                    }
                    else
                    {
                        // Keep discovered markers enabled
                        MarkerEnable(state.Locator, state.OriginalIconScale);
                    }

                    // Step 11.4: If undiscovered, create a trigger and collider for the marker if it doesn't already have one
                    if (state.TriggerObject == null &&
                        _originalMarkerStates.ContainsKey(marker.Key) &&
                        _originalMarkerStates[marker.Key])
                    {
                        state.TriggerObject = CreateProximityTrigger(state.Locator.gameObject, marker.Key);
                        if (state.TriggerObject != null)
                        {
                            triggerCount++;
                    }
                    }

                    // 11.5 Always enable GPSLocator for bunkers when managed
                    if (marker.Key.Contains("Bunker"))
                    {
                        state.Locator.Enable(true);
                    }
                }
                else
                {
                    // Step 11.5: Re-enable marker
                    MarkerEnable(state.Locator, state.OriginalIconScale);
                    // RLog.Debug($"Toggle {typeIdentifier} OFF: Saved {marker.Key} IsDisabled={_originalMarkerStates[marker.Key]}, Set IsDisabled={state.IsDisabled}");
                    affectedCount++;

                    // Step 11.6: Destroy the trigger and collider if they exist
                    if (state.TriggerObject != null)
                    {
                        UnityEngine.Object.Destroy(state.TriggerObject);
                        state.TriggerObject = null;
                    }

                    // 11.7 Always disable GPSLocator for bunkers when unmanaged
                    if (marker.Key.Contains("Bunker"))
                    {
                        state.Locator.Enable(false);
                    }
                }
            }
        }

        // Step 11.7: Log a summary of the marker state changes
        if (affectedCount > 0)
        {
            string action = shouldManage ? "Disabled" : "Enabled";
            string typeName = typeIdentifier == "Cave" ? "cave" : typeIdentifier == "GPSLocatorPickup" ? "Team B" : "bunker";
            RLog.Msg($"{action} {affectedCount} {typeName} markers due to config change");
        }

        // Step 11.8: Log a summary of triggers created
        if (triggerCount > 0)
        {
            string typeName = typeIdentifier == "Cave" ? "cave" : typeIdentifier == "GPSLocatorPickup" ? "Team B" : "bunker";
            RLog.Msg($"Added {triggerCount} ProximityTriggers with SphereColliders for {typeName} markers");
        }

        //RLog.Debug($"_originalMarkerStates after update: {string.Join(", ", _originalMarkerStates.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        //RLog.Debug($"Markers after update: {string.Join(", ", Markers.Where(kvp => kvp.Key.Contains("Cave")).Select(kvp => $"{kvp.Key}={kvp.Value.IsDisabled}"))}");

    }

    // Updates all GPS locators' proximity beep state and beep radius based on current config.
    // Enables beeping for undiscovered, managed markers and sets beep range as specified by the user.
    // Called on game start and whenever Proximity Beep settings change.
    public static void UpdateProximityBeepStates()
    {
        RLog.Debug("UpdateProximityBeepStates called.");

        int totalMarkers = 0;
        int undiscoveredWithBeep = 0;
        int discoveredNoBeep = 0;
        int skipped = 0;

        CleanMarkerDictionary();

        foreach (var marker in Markers)
        {
            GPSLocatorState state = marker.Value;
            GPSLocator locator = state.Locator;
            float safeBeepRadius = Mathf.Clamp(Config.BeepRadius.Value, 1.0f, 500.0f);
            locator._beepMaxRange = safeBeepRadius;

            if (!IsMarkerTypeManaged(marker.Key))
            {
                // All unmanaged markers have beep OFF, regardless of discovery status
                locator._shouldBeepWhenInRange = false;
                skipped++;
                continue;
            }

            if (state == null || state.Locator == null)
            {
                RLog.Debug($"Skipped marker '{marker.Key}' (state or locator missing)");
                skipped++;
                continue;
            }

            if (!Config.ProximityBeep.Value)
            {
                // Proximity beep disabled: ensure beeping is off for all markers
                locator._shouldBeepWhenInRange = false;
                // RLog.Debug($"Marker '{marker.Key}': Proximity beep OFF globally -> beep disabled");
                discoveredNoBeep++;
            }
            else
            {
                if (!state.IsDisabled)
                {
                    locator._shouldBeepWhenInRange = false;
                    discoveredNoBeep++;
                    // RLog.Debug($"Marker '{marker.Key}': discovered -> beep disabled");
                }
                else
                {
                    locator._shouldBeepWhenInRange = true;
                    undiscoveredWithBeep++;
                    // RLog.Debug($"Marker '{marker.Key}': undiscovered -> beep enabled (radius {Config.BeepRadius.Value})");
                }
            }
            totalMarkers++;
        }

        RLog.Debug($"Proximity beep update complete. Markers processed: {totalMarkers}, Beep enabled for: {undiscoveredWithBeep}, Beep disabled for: {discoveredNoBeep}, Skipped: {skipped}");
    }

    // Updates all GPS locators' proximity beep state and beep radius based on current config.
    public static void UpdateIconPulseStates()
    {
        CleanMarkerDictionary();

        foreach (var marker in BlankGPS.Markers)
        {
            GPSLocator locator = marker.Value.Locator;
            if (locator == null) continue;

            // Only affect bunker markers
            if (marker.Key.Contains("Bunker"))
            {
                locator._pulseIcon = !Config.DisableIconPulse.Value;
                locator.ForceRefresh(); // If available, ensures state applies visually
            }
        }
    }

    public static void UpdateTagBeepStates()
    {
        int updated = 0;

        foreach (var locator in GameObject.FindObjectsOfType<GPSLocator>())
        {
            if (locator == null || locator.gameObject == null)
                continue;

            if (locator.gameObject.name != "GPSLocatorHeld")
                continue;

            if (Config.DisableTakenLocatorBeep.Value)
            {
                locator._shouldBeepWhenInRange = false;
                RLog.Debug("[BlankGPS] UpdateTagBeepStates -> GPSLocatorHeld beep DISABLED via config");
            }
            else
            {
                locator._shouldBeepWhenInRange = true;
                RLog.Debug("[BlankGPS] UpdateTagBeepStates -> GPSLocatorHeld beep ENABLED via config");
            }

            updated++;
        }

        if (updated > 0)
        {
            RLog.Msg($"[BlankGPS] Updated {updated} GPSLocatorHeld instance(s) via UpdateTagBeepStates()");
        }
        else
        {
            RLog.Msg("[BlankGPS] No active GPSLocatorHeld found during UpdateTagBeepStates()");
        }
    }

    // Step 12: Creates a proximity trigger for a GPSLocator
    public static GameObject CreateProximityTrigger(GameObject gpsObject, string markerKey)
    {
        // Step 12.1: Create a child GameObject for the proximity trigger
        GameObject triggerObject = new GameObject($"ProximityTrigger_{gpsObject.name}");

        // Step 12.2: Make triggerObject a child of gpsObject without preserving world position
        triggerObject.transform.SetParent(gpsObject.transform, false);

        // Step 12.3: Add a SphereCollider to triggerObject
        SphereCollider collider = triggerObject.AddComponent<SphereCollider>();
        if (collider == null)
        {
            RLog.Error($"Failed to add SphereCollider to {triggerObject.name}!");
            return null;
        }

        // Step 12.4: Configure the SphereCollider
        float safeDiscoveryRadius = Mathf.Clamp(Config.DiscoveryRadius.Value, 1.0f, 20.0f);
        collider.radius = safeDiscoveryRadius;
        collider.isTrigger = true;

        // Step 12.5: Attach the ProximityTrigger component to triggerObject
        ProximityTrigger trigger = triggerObject.AddComponent<ProximityTrigger>();
        if (trigger == null)
        {
            RLog.Error($"Failed to add ProximityTrigger to {triggerObject.name}!");
            UnityEngine.Object.Destroy(triggerObject);
            return null;
        }
        trigger._markerKey = markerKey;
        return triggerObject;
    }

    // Step 12.6: Recreate all proximity triggers for managed markers
    // Destroys existing proximity triggers and creates new ones with the current DiscoveryRadius value.
    // Called when the proximity radius slider is changed, to ensure triggers use updated settings.
    public static void RecreateAllProximityTriggers()
    {
        CleanMarkerDictionary();

        int caveTriggerCount = 0;
        int teamBTriggerCount = 0;
        int bunkerTriggerCount = 0;

        foreach (var marker in Markers)
        {
            var state = marker.Value;
            // Destroy existing trigger if present
            if (state.TriggerObject != null)
            {
                UnityEngine.Object.Destroy(state.TriggerObject);
                state.TriggerObject = null;
            }
            // Only recreate trigger if marker type is managed, ProximityDiscovery is enabled, AND marker is undiscovered
            if (
                IsMarkerTypeManaged(marker.Key)
                && Config.ProximityDiscovery.Value
                && _originalMarkerStates.ContainsKey(marker.Key)
                && _originalMarkerStates[marker.Key] // only undiscovered
            )
            {
                state.TriggerObject = CreateProximityTrigger(state.Locator.gameObject, marker.Key);

                // === Trigger counters for different marker types ===
                if (marker.Key.Contains("Cave"))
                    caveTriggerCount++;
                else if (marker.Key.Contains("GPSLocatorPickup"))
                    teamBTriggerCount++;
                else if (marker.Key.Contains("Bunker"))
                    bunkerTriggerCount++;
            }
        }

        // Optional: log the results
        RLog.Msg($"Added {caveTriggerCount} ProximityTriggers with SphereColliders for cave markers");
        RLog.Msg($"Added {teamBTriggerCount} ProximityTriggers with SphereColliders for Team B markers");
        RLog.Msg($"Added {bunkerTriggerCount} ProximityTriggers with SphereColliders for bunker markers");
        //RLog.Debug($"_originalMarkerStates on game start: {string.Join(", ", _originalMarkerStates.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        //RLog.Debug($"Markers on game start: {string.Join(", ", Markers.Where(kvp => kvp.Key.Contains("Cave")).Select(kvp => $"{kvp.Key}={kvp.Value.IsDisabled}"))}");
    }

    public BlankGPS()
    {
        // Step 13: Initialize the marker list with all target markers
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

        // Step 13.1: Log the number of markers to confirm the list is initialized
        RLog.Msg($"Initialized {_defaultMarkers.Count} markers to disable");
        //RLog.Debug($"_originalMarkerStates on init: {string.Join(", ", _originalMarkerStates.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

        // Step 14: Enable Harmony patching for our mod
        // This tells RedLoader to apply all Harmony patches defined in our assembly (e.g., GPSLocatorAwakePatch)
        HarmonyPatchAll = true;
    }

    // Step 15: Initialize the mod and its settings
    protected override void OnInitializeMod()
    {
        // Step 15.1: Initialize configuration settings
        RLog.Debug("Initializing BlankGPS config...");
        Config.Init();
        RLog.Debug("BlankGPS config initialized");

        // Step 15.2: Register the save manager
        var saveManager = new BlankGPSSaveManager();
        SonsSaveTools.Register(saveManager);
        RLog.Debug("Registered BlankGPSSaveManager");

        SdkEvents.OnWorldExited.Subscribe(OnWorldExitedCallback);
    }

    protected override void OnSdkInitialized()
    {
        // Step 15.3: Create the in-game settings UI and initialize the BlankGPS UI
        RLog.Debug("Creating BlankGPS UI...");
        BlankGPSUi.Create();
        RLog.Debug("BlankGPS UI created");
        SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected override void OnGameStart()
    {
        CleanMarkerDictionary();
        _originalMarkerStates.Clear();
        InitializeOriginalMarkerStates();
        RecreateAllProximityTriggers();

        // Step 17.1 Apply marker states based on config
        UpdateMarkerStatesForType("Cave", Config.ManageCaves.Value);
        UpdateMarkerStatesForType("GPSLocatorPickup", Config.ManageTeamB.Value);
        UpdateMarkerStatesForType("Bunker", Config.ManageBunkers.Value);

        // Step 17.2: Update all proximity beep states according to current config
        UpdateProximityBeepStates();

        // Step 17.3 Update all icon pulse states according to current config
        UpdateIconPulseStates();

        // Step 17.4 Update all disable tag states according to current config
        UpdateTagBeepStates();
    }

    // Marker state cleanup on world exit
    private void OnWorldExitedCallback()
    {
        BlankGPS.CleanMarkerDictionary();
        BlankGPS._originalMarkerStates.Clear();
        BlankGPS._loadedMarkerStates.Clear();
    }
}

// Step 18: Harmony patch for GPSLocator.OnEnable
// This patch runs custom code whenever a GPSLocator component is enabled in the game
// GPSLocator components control GPS markers (e.g., CaveAEntranceGPS), and OnEnable is called when the marker is loaded
[HarmonyPatch(typeof(GPSLocator), "OnEnable")]
public class GPSLocatorAwakePatch
{
    // Step 19: Find all markers in our list that match the GameObject name
    // We use LINQ’s Where to get all marker tuples with a matching name
    // This ensures we check all entries, not just the ones for GPSLocatorPickup with different positions
    [HarmonyPostfix]
    public static void Postfix(GPSLocator __instance)
    {
        // Defensive guard: skip all logic if this is a held GPS tag
        if (__instance.gameObject.name == "GPSLocatorHeld")
            return;

        var matchingMarkers = BlankGPS.DefaultMarkers.Where(marker => marker.gameObjectName == __instance.gameObject.name);
        if (!matchingMarkers.Any()) return;

        // Step 20: Iterate over all matching markers to find the correct one
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

                // Compare the fetched position with the stored position using a small distance threshold
                matches = Vector3.Distance(positionValue, matchingMarker.position) < 50f;
            }

            if (matches)
            {
                // Step 21: Determine if the marker type should be managed based on config settings
                bool shouldDisable = BlankGPS.IsMarkerTypeManaged(__instance.gameObject.name);

                // Step 22: Check for a saved state in _loadedMarkerStates only if the marker type is managed
                string key = matchingMarker.gameObjectName == "GPSLocatorPickup" ? $"{__instance.gameObject.name}_{matchingMarker.position}" : __instance.gameObject.name;
                if (shouldDisable && BlankGPS._loadedMarkerStates.TryGetValue(key, out bool loadedIsDisabled))
                {
                    shouldDisable = loadedIsDisabled;
                }
                else if (shouldDisable)
                {
                    // Default to disabled for managed markers with no saved state
                    shouldDisable = true;
                }
                //RLog.Debug($"Postfix {key}: shouldDisable={shouldDisable}, _loadedMarkerStates={(BlankGPS._loadedMarkerStates.ContainsKey(key) ? BlankGPS._loadedMarkerStates[key].ToString() : "none")}, _originalMarkerStates={(BlankGPS._originalMarkerStates.ContainsKey(key) ? BlankGPS._originalMarkerStates[key].ToString() : "none")}");

                // Step 23: Apply the appropriate state to the marker
                //if (shouldDisable)
                //{
                //    BlankGPS.MarkerDisable(__instance);
                //}
                //else
                //{
                //    BlankGPS.MarkerEnable(__instance, matchingMarker.iconScale);
                //}

                // Step 24: Add the GPSLocator to the dictionary of managed markers
                // Create a GPSLocatorState object and store it in the dictionary
                GPSLocatorState state = new GPSLocatorState
                {
                    Locator = __instance,
                    IsDisabled = shouldDisable,
                    OriginalIconScale = matchingMarker.iconScale,
                    TriggerObject = null // Initialize with no trigger (will be set in OnGameStart if managed)
                };

                BlankGPS.Markers[key] = state;

                // Step 25: Handle bunker enable/disable
                //if (key.Contains("Bunker"))
                //{
                //    if (BlankGPS.IsMarkerTypeManaged(key))
                //    {
                //        __instance.Enable(true);  // Enable for managed bunkers
                //    }
                //    else
                //    {
                //        __instance.Enable(false); // Disable for unmanaged bunkers
                //    }
                //}

                //RLog.Debug($"Postfix set Markers[{key}].IsDisabled={state.IsDisabled}");

                // Break after processing the marker, as we’ve found the correct match
                break;
            }
        }
        //RLog.Debug($"Markers after Postfix: {string.Join(", ", BlankGPS.Markers.Where(kvp => kvp.Key.Contains("Cave")).Select(kvp => $"{kvp.Key}={kvp.Value.IsDisabled}"))}");
    }
}

[HarmonyPatch(typeof(GPSLocator), "Start")]
public class GPSLocatorStartPatch
{
    [HarmonyPostfix]
    public static void Postfix(GPSLocator __instance)
    {
        if (__instance.gameObject.name == "GPSLocatorHeld")
        {
            __instance.StartCoroutine(ApplyBeepConfigAfterUnityInit(__instance));
        }
    }

    private static System.Collections.IEnumerator ApplyBeepConfigAfterUnityInit(GPSLocator locator)
    {
        float maxWait = 3f;
        float elapsed = 0f;
        float interval = 0.1f;

        RLog.Debug("[BlankGPS] Waiting for GPSLocatorHeld to be initialized by Unity...");

        // Step 1: Wait until Unity sets _shouldBeepWhenInRange = true
        while (elapsed < maxWait)
        {
            if (locator == null || locator.gameObject == null)
                yield break;

            if (locator._shouldBeepWhenInRange)
            {
                RLog.Debug("[BlankGPS] GPSLocatorHeld initialized (beep = true). Applying config control...");
                break;
            }

            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        if (locator == null || locator.gameObject == null)
            yield break;

        // Step 2: Apply config setting only after Unity has stepped in
        bool desired = !Config.DisableTakenLocatorBeep.Value;
        if (locator._shouldBeepWhenInRange == desired)
        {
            RLog.Debug($"[BlankGPS] Beep state already matches config ({desired}) — no enforcement needed.");
            yield break;
        }

        // Step 3: Enforce config value for up to X seconds until it sticks
        elapsed = 0f;
        RLog.Debug($"[BlankGPS] Enforcing beep value: {desired.ToString().ToUpper()} for up to {maxWait}s");

        while (elapsed < maxWait)
        {
            if (locator == null || locator.gameObject == null)
                yield break;

            locator._shouldBeepWhenInRange = desired;

            // Exit early if it sticks immediately
            yield return new WaitForSeconds(interval);
            if (locator._shouldBeepWhenInRange == desired)
            {
                RLog.Debug("[BlankGPS] Beep state enforced successfully.");
                yield break;
            }

            elapsed += interval;
        }

        RLog.Debug("[BlankGPS] Timeout – Final beep state: " + locator._shouldBeepWhenInRange.ToString().ToUpper());
    }
}