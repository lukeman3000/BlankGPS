using SonsSdk;
using Sons;
using RedLoader;
using Sons.Gameplay.GPS;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace BlankGPS;

public class BlankGPS : SonsMod
{
    private bool _hasDisabledIcons = false; // Flag to run the logic only once
    private List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)> _markersToDisable; // List of markers to disable
    private List<string> _processedMarkers; // Track which markers have been processed
    private bool _gameActivated = false; // Flag to indicate if the game is activated
    private float _timeSinceGameActivated = 0f; // Track time since the game was activated
    private float _timeWaitingForActivation = 0f; // Track time waiting for game activation
    private float _timeSinceFirstMarkerFound = 0f; // Track time since the first marker was found
    private bool _firstMarkerFound = false; // Flag to indicate if at least one marker has been found
    private const float MAX_TIME_AFTER_GAME_ACTIVATED = 15f; // Maximum time to search after game activation (in seconds)
    private const float MAX_TIME_TO_WAIT_FOR_ACTIVATION = 30f; // Maximum time to wait for game activation (in seconds)
    private const float INITIAL_SEARCH_PERIOD = 1f; // Initial period to search for all markers after finding the first one (in seconds)

    public BlankGPS()
    {
        OnUpdateCallback = Update; // Run the Update method every frame

        // Hardcode the list of markers to disable (removed NonExistentMarker)
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
    }

    protected override void OnSdkInitialized()
    {
        BlankGPSUi.Create();
        // SettingsRegistry.CreateSettings(this, null, typeof(Config));
    }

    protected void Update()
    {
        // Only run the logic once
        if (_hasDisabledIcons)
        {
            OnUpdateCallback = null; // Unsubscribe from OnUpdate to stop the loop
            return;
        }

        // Check if the game is activated by looking for the LocalPlayer GameObject
        if (!_gameActivated)
        {
            _timeWaitingForActivation += Time.deltaTime;
            if (_timeWaitingForActivation >= MAX_TIME_TO_WAIT_FOR_ACTIVATION)
            {
                RLog.Error($"Failed to detect game activation (LocalPlayer GameObject not found) after {MAX_TIME_TO_WAIT_FOR_ACTIVATION} seconds. Stopping the search.");
                _hasDisabledIcons = true;
                return;
            }

            GameObject playerObject = GameObject.Find("LocalPlayer");
            if (playerObject != null)
            {
                RLog.Msg("Game activated: LocalPlayer GameObject found. Starting marker search.");
                _gameActivated = true;
                _timeSinceGameActivated = 0f; // Reset the timer
            }
            else
            {
                RLog.Warning("Game not yet activated: LocalPlayer GameObject not found. Waiting to search for markers...");
                return;
            }
        }

        // Increment the timer since the game was activated
        _timeSinceGameActivated += Time.deltaTime;

        // Find all GPSLocator components in the scene
        GPSLocator[] locators = GameObject.FindObjectsOfType<GPSLocator>();
        if (locators.Length == 0)
        {
            RLog.Warning("No GPSLocator components found yet. Will keep trying...");
            if (_timeSinceGameActivated >= MAX_TIME_AFTER_GAME_ACTIVATED)
            {
                RLog.Error($"Failed to find any GPSLocator components {MAX_TIME_AFTER_GAME_ACTIVATED} seconds after game activation. Stopping the search.");
                _hasDisabledIcons = true;
            }
            return;
        }

        bool allMarkersProcessed = true;

        foreach (var marker in _markersToDisable)
        {
            // Generate a unique key for this marker to track if it has been processed
            string markerKey = marker.isMethod
                ? $"{marker.gameObjectName}_{marker.identifierProperty}_{marker.identifierValue}"
                : $"{marker.gameObjectName}_{marker.identifierProperty}_{marker.identifierValue}";

            // Skip if this marker has already been processed
            if (_processedMarkers.Contains(markerKey))
            {
                continue;
            }

            bool markerProcessed = false;

            foreach (var locator in locators)
            {
                // Check if this GPSLocator matches the marker's GameObject name
                if (locator.gameObject.name == marker.gameObjectName)
                {
                    // Use reflection to access the identifier (property or method)
                    object identifierValue = null;
                    if (marker.isMethod)
                    {
                        // Handle as a method
                        var identifierMethod = typeof(GPSLocator).GetMethod(marker.identifierProperty, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                        if (identifierMethod != null)
                        {
                            identifierValue = identifierMethod.Invoke(locator, null);
                        }
                        else
                        {
                            RLog.Error($"Could not find {marker.identifierProperty} method on GPSLocator for {marker.gameObjectName}.");
                            continue;
                        }
                    }
                    else
                    {
                        // Handle as a property
                        var identifierProperty = typeof(GPSLocator).GetProperty(marker.identifierProperty, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (identifierProperty != null)
                        {
                            identifierValue = identifierProperty.GetValue(locator);
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
                    else
                    {
                        matches = Equals(identifierValue, marker.identifierValue);
                    }

                    if (matches)
                    {
                        // Log the identifier value only when we find a match
                        if (identifierValue is float floatValueLog)
                        {
                            RLog.Msg($"GPSLocator component on {marker.gameObjectName} has _iconScale: {floatValueLog}");
                        }
                        else if (identifierValue is Vector3 vectorValueLog)
                        {
                            RLog.Msg($"GPSLocator component on {marker.gameObjectName} has position: {vectorValueLog}");
                        }
                        else
                        {
                            RLog.Msg($"GPSLocator component on {marker.gameObjectName} has {marker.identifierProperty}: {identifierValue}");
                        }

                        // Use reflection to access _usePlayerAreaMask
                        var usePlayerAreaMaskProperty = typeof(GPSLocator).GetProperty("_usePlayerAreaMask", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (usePlayerAreaMaskProperty != null)
                        {
                            usePlayerAreaMaskProperty.SetValue(locator, true);
                            RLog.Msg($"Disabled {marker.gameObjectName} icon on GPS using reflection ({marker.identifierProperty} = {marker.identifierValue}).");
                            _processedMarkers.Add(markerKey); // Mark this marker as processed
                            markerProcessed = true;

                            // Track the time since the first marker was found
                            if (!_firstMarkerFound)
                            {
                                _firstMarkerFound = true;
                                _timeSinceFirstMarkerFound = 0f;
                            }
                            break; // Exit loop once found
                        }
                        else
                        {
                            RLog.Error($"Could not find _usePlayerAreaMask property on GPSLocator for {marker.gameObjectName}.");
                        }
                    }
                }
            }

            if (!markerProcessed)
            {
                RLog.Warning($"Could not find {marker.gameObjectName} with matching {marker.identifierProperty} = {marker.identifierValue}. Will keep trying...");
                allMarkersProcessed = false; // Keep trying if any marker is not found

                // If we've found at least one marker and the initial search period has passed, assume remaining markers don't exist
                if (_firstMarkerFound)
                {
                    _timeSinceFirstMarkerFound += Time.deltaTime;
                    if (_timeSinceFirstMarkerFound >= INITIAL_SEARCH_PERIOD)
                    {
                        RLog.Warning($"Marker {marker.gameObjectName} not found after initial search period ({INITIAL_SEARCH_PERIOD} seconds). Assuming it doesn't exist and stopping the search.");
                        _hasDisabledIcons = true;
                        break;
                    }
                }

                // If the overall timeout is reached, stop the search
                if (_timeSinceGameActivated >= MAX_TIME_AFTER_GAME_ACTIVATED)
                {
                    RLog.Error($"Failed to find {marker.gameObjectName} with matching {marker.identifierProperty} = {marker.identifierValue} {MAX_TIME_AFTER_GAME_ACTIVATED} seconds after game activation. Stopping the search.");
                    _hasDisabledIcons = true;
                    break;
                }
            }
        }

        if (allMarkersProcessed)
        {
            RLog.Msg("All markers processed successfully. Stopping the search.");
            _hasDisabledIcons = true; // Set flag to stop the OnUpdate loop
        }
    }
}