using RedLoader;
using SUI;

namespace BlankGPS;

public static class Config
{
    public static ConfigCategory Category { get; private set; }

    // Toggle for proximity-based marker enabling
    public static ConfigEntry<bool> ProximityDiscovery { get; private set; }

    // Proximity radius in units
    public static ConfigEntry<float> DiscoveryRadius { get; private set; }

    // Last proximity radius, used to detect changes when the user adjusts the slider (for live trigger updates)
    private static float _lastDiscoveryRadius;

    // Toggle for proximity beep feature
    public static ConfigEntry<bool> ProximityBeep { get; private set; }

    // Proximity beep radius in units
    public static ConfigEntry<float> BeepRadius { get; private set; }

    // Toggle for managing cave entrance markers
    public static ConfigEntry<bool> ManageCaves { get; private set; }

    // Toggle for managing Team B markers (GPSLocatorPickup)
    public static ConfigEntry<bool> ManageTeamB { get; private set; }

    // Toggle for managing bunker entrance markers
    public static ConfigEntry<bool> ManageBunkers { get; private set; }

    // Toggle for icon pulse
    public static ConfigEntry<bool> DisableIconPulse { get; private set; }

    public static ConfigEntry<bool> DisableTakenLocatorBeep { get; private set; }

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
        ProximityDiscovery = Category.CreateEntry(
            "proximity_discovery",
            true,
            "Proximity Discovery",
            "Enable proximity-based marker enabling (markers are hidden until approached).");

        DiscoveryRadius = Category.CreateEntry(
            "discovery_radius",
            10.0f,
            "Discovery Radius",
            "Distance (in units) at which markers enable when approached.");
        DiscoveryRadius.SetRange(1.0f, 10.0f);

        ProximityBeep = Category.CreateEntry(
            "proximity_beep",
            true,
            "Proximity Beep",
            "Enable proximity-based beep for undiscovered markers.");

        BeepRadius = Category.CreateEntry(
            "beep_radius",
            250.0f,
            "Beep Radius",
            "Distance (in units) at which the proximity beep can be heard from any given undiscovered marker.");
        BeepRadius.SetRange(1.0f, 250.0f);

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

        DisableIconPulse = Category.CreateEntry(
            "disable_icon_pulse",
            true,
            "Disable Bunker Icon Pulse",
            "Disable icon pulse for bunker markers.");

        DisableTakenLocatorBeep = Category.CreateEntry(
            "disable_taken_locator_beep",
            true,
            "Disable taken GPS Locator beep",
            "Disables beeping for Team B GPS Locators after they've been taken.");

        // Initialize previous values to match defaults
        _lastManageCaves = ManageCaves.Value;
        _lastManageTeamB = ManageTeamB.Value;
        _lastManageBunkers = ManageBunkers.Value;
        _lastDiscoveryRadius = DiscoveryRadius.Value;
    }

    // Called when the settings UI is closed to update marker states based on config changes
    public static void OnSettingsUiClosed()
    {
        // Check for changes in ManageCaves
        if (_lastManageCaves != ManageCaves.Value)
        {
            BlankGPS.UpdateMarkerStatesForType("Cave", ManageCaves.Value);
            _lastManageCaves = ManageCaves.Value;
        }

        // Check for changes in ManageTeamB
        if (_lastManageTeamB != ManageTeamB.Value)
        {
            BlankGPS.UpdateMarkerStatesForType("GPSLocatorPickup", ManageTeamB.Value);
            _lastManageTeamB = ManageTeamB.Value;
        }

        // Check for changes in ManageBunkers
        if (_lastManageBunkers != ManageBunkers.Value)
        {
            BlankGPS.UpdateMarkerStatesForType("Bunker", ManageBunkers.Value);
            _lastManageBunkers = ManageBunkers.Value;
        }

        // Check for proximity_radius changes
        // If the proximity radius slider changed, remake all triggers so collider radii match current settings
        if (_lastDiscoveryRadius != DiscoveryRadius.Value)
        {
            BlankGPS.RecreateAllProximityTriggers();
            _lastDiscoveryRadius = DiscoveryRadius.Value;
        }

        // Update all proximity beep states to reflect the new config
        BlankGPS.UpdateProximityBeepStates();

        // Update all icon pulse states to reflect the new config
        BlankGPS.UpdateIconPulseStates();

        // Update all disable tag beep states to reflect the new config
        BlankGPS.UpdateTagBeepStates();
    }
}