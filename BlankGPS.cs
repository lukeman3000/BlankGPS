using SonsSdk;
using Sons;
using RedLoader;
using Sons.Gameplay.GPS;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.IO; // For file operations
using System.Text; // For StringBuilder
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
        // Step 1: Define the default marker list
        var defaultMarkers = new List<(string gameObjectName, string identifierProperty, object identifierValue, bool isMethod)>
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

        // Step 2: Load the marker list from a JSON file in the mod folder's BlankGPS subfolder
        string modsFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string subFolder = Path.Combine(modsFolder, "BlankGPS");
        string configPath = Path.Combine(subFolder, "BlankGPS.json");

        try
        {
            // Create the BlankGPS subfolder if it doesn't exist
            if (!Directory.Exists(subFolder))
            {
                Directory.CreateDirectory(subFolder);
                RLog.Msg($"Created BlankGPS subfolder at {subFolder}.");
            }

            if (!File.Exists(configPath))
            {
                // Step 3: Manually construct the JSON file if it doesn't exist
                var sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < defaultMarkers.Count; i++)
                {
                    var marker = defaultMarkers[i];
                    sb.AppendLine("  {");
                    sb.AppendLine($"    \"GameObjectName\": \"{marker.gameObjectName}\",");
                    sb.AppendLine($"    \"IdentifierProperty\": \"{marker.identifierProperty}\",");
                    sb.AppendLine($"    \"IsMethod\": {marker.isMethod.ToString().ToLower()}");

                    if (marker.isMethod)
                    {
                        var position = (Vector3)marker.identifierValue;
                        sb.AppendLine($"    \"Position\": [{position.x}, {position.y}, {position.z}]");
                    }
                    else
                    {
                        sb.AppendLine($"    \"IconScale\": {marker.identifierValue}");
                    }

                    if (i < defaultMarkers.Count - 1)
                    {
                        sb.AppendLine("  },");
                    }
                    else
                    {
                        sb.AppendLine("  }");
                    }
                }
                sb.AppendLine("]");
                File.WriteAllText(configPath, sb.ToString());
                RLog.Msg($"Created configuration file at {configPath}.");
            }

            // Step 4: Manually parse the JSON file
            string jsonContent = File.ReadAllText(configPath);
            jsonContent = jsonContent.Trim();
            if (!jsonContent.StartsWith("[") || !jsonContent.EndsWith("]"))
            {
                throw new System.Exception("Invalid JSON format: Expected a JSON array.");
            }

            // Remove the outer brackets and split into individual marker entries
            jsonContent = jsonContent.Substring(1, jsonContent.Length - 2).Trim();
            var markerEntries = new List<string>();
            int braceCount = 0;
            int startIndex = 0;
            for (int i = 0; i < jsonContent.Length; i++)
            {
                if (jsonContent[i] == '{')
                {
                    braceCount++;
                }
                else if (jsonContent[i] == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        markerEntries.Add(jsonContent.Substring(startIndex, i - startIndex + 1).Trim());
                        startIndex = i + 2; // Skip the comma and whitespace
                    }
                }
            }

            // Parse each marker entry
            _markersToDisable = new List<(string, string, object, bool)>();
            foreach (var entry in markerEntries)
            {
                var markerDict = new Dictionary<string, string>();
                var lines = entry.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("{") || trimmedLine.StartsWith("}"))
                        continue;

                    var parts = trimmedLine.Split(new[] { ':' }, 2);
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim().Trim('"');
                    var value = parts[1].Trim().Trim(',').Trim();
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    markerDict[key] = value;
                }

                string gameObjectName = markerDict["GameObjectName"];
                string identifierProperty = markerDict["IdentifierProperty"];
                bool isMethod = bool.Parse(markerDict["IsMethod"]);
                object identifierValue;

                if (isMethod)
                {
                    string positionStr = markerDict["Position"].Trim();
                    positionStr = positionStr.Substring(1, positionStr.Length - 2); // Remove [ and ]
                    var positionParts = positionStr.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                    float x = float.Parse(positionParts[0].Trim());
                    float y = float.Parse(positionParts[1].Trim());
                    float z = float.Parse(positionParts[2].Trim());
                    identifierValue = new Vector3(x, y, z);
                }
                else
                {
                    identifierValue = float.Parse(markerDict["IconScale"]);
                }

                _markersToDisable.Add((gameObjectName, identifierProperty, identifierValue, isMethod));
            }
            RLog.Msg($"Loaded marker list from {configPath} with {_markersToDisable.Count} markers.");
        }
        catch (System.Exception e)
        {
            // Step 5: Fall back to the hardcoded list if loading or writing fails
            RLog.Error($"Failed to load or create configuration file at {configPath}: {e.Message}. Using hardcoded marker list.");
            _markersToDisable = defaultMarkers;
        }

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