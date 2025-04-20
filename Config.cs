using RedLoader;
using SUI;

namespace BlankGPS;

public static class Config
{
    public static ConfigCategory Category { get; private set; }

    // Toggle for proximity-based marker enabling
    public static ConfigEntry<bool> ProximityEnabled { get; private set; }

    // Proximity radius in units
    public static ConfigEntry<float> ProximityRadius { get; private set; }

    // Toggle for managing cave entrance markers
    public static ConfigEntry<bool> ManageCaves { get; private set; }

    // Toggle for managing Team B markers (GPSLocatorPickup)
    public static ConfigEntry<bool> ManageTeamB { get; private set; }

    // Toggle for managing bunker entrance markers
    public static ConfigEntry<bool> ManageBunkers { get; private set; }

    // Auto populated after calling SettingsRegistry.CreateSettings...
    private static SettingsRegistry.SettingsEntry _settingsEntry;

    // Previous values for detecting changes in OnSettingsUiClosed
    private static bool _lastManageCaves;
    private static bool _lastManageTeamB;
    private static bool _lastManageBunkers;

    public static void Init()
    {
        Category = ConfigSystem.CreateFileCategory("BlankGPS", "BlankGPS", "BlankGPS.cfg");

        // Initialize settings with default values and descriptions
        ProximityEnabled = Category.CreateEntry(
            "proximity_enabled",
            true,
            "Proximity Enabled",
            "Enable proximity-based marker enabling (markers are hidden until approached).");

        ProximityRadius = Category.CreateEntry(
            "proximity_radius",
            10.0f,
            "Proximity Radius",
            "Distance (in units) at which markers enable when approached.");

        ManageCaves = Category.CreateEntry(
            "manage_caves",
            true,
            "Manage Caves",
            "Allow BlankGPS to manage cave entrance markers (hides them until approached if enabled).");

        ManageTeamB = Category.CreateEntry(
            "manage_team_b",
            true,
            "Manage Team B",
            "Allow BlankGPS to manage Team B markers (hides them until approached if enabled).");

        ManageBunkers = Category.CreateEntry(
            "manage_bunkers",
            true,
            "Manage Bunkers",
            "Allow BlankGPS to manage bunker entrance markers (hides them until approached if enabled).");

        // Initialize previous values to match defaults
        _lastManageCaves = ManageCaves.Value;
        _lastManageTeamB = ManageTeamB.Value;
        _lastManageBunkers = ManageBunkers.Value;
    }

    // Same as the callback in "CreateSettings". Called when the settings ui is closed.
    public static void OnSettingsUiClosed()
    {
        // Placeholder for future real-time update logic
    }
}