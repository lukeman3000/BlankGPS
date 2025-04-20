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

    // Auto populated after calling SettingsRegistry.CreateSettings...
    private static SettingsRegistry.SettingsEntry _settingsEntry;

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
    }

    // Same as the callback in "CreateSettings". Called when the settings ui is closed.
    public static void OnSettingsUiClosed()
    {
    }
}