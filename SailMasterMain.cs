using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SailMaster
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class SailMasterMain : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        internal static ConfigEntry<KeyboardShortcut> allSailsKey;
        internal static ConfigEntry<KeyboardShortcut> group1SailsKey;
        internal static ConfigEntry<KeyboardShortcut> group2SailsKey;
        internal static ConfigEntry<KeyboardShortcut> group3SailsKey;
        internal static ConfigEntry<KeyboardShortcut> group4SailsKey;
        internal static ConfigEntry<KeyboardShortcut> group5SailsKey;
        internal static ConfigEntry<KeyboardShortcut> group6SailsKey;
        internal static ConfigEntry<KeyboardShortcut> toggleGuiKey;
        internal static ConfigEntry<float> hoistingSpeed;
        internal static ConfigEntry<float> navigationKp;
        internal static ConfigEntry<float> navigationKi;
        internal static ConfigEntry<float> navigationKd;
        internal static ConfigEntry<float> autoTrimIntervalSeconds;
        internal static ConfigEntry<bool> showGuiOnStart;
        internal static ConfigEntry<bool> timingProfilerLog;
        internal static ConfigEntry<float> timingProfilerIntervalSeconds;
        internal static ConfigEntry<int> simulatedSailRows;
        internal static ConfigEntry<int> simulatedTrimControlsPerSail;

        private SailMasterGui gui;

        private void Awake()
        {
            Logger = base.Logger;

            allSailsKey = Config.Bind("Hotkeys", "All Sails", new KeyboardShortcut(KeyCode.Alpha0), "Raise or lower all controllable sails.");
            group1SailsKey = Config.Bind("Hotkeys", "Group 1", new KeyboardShortcut(KeyCode.Alpha1), "Raise or lower SailMaster group 1.");
            group2SailsKey = Config.Bind("Hotkeys", "Group 2", new KeyboardShortcut(KeyCode.Alpha2), "Raise or lower SailMaster group 2.");
            group3SailsKey = Config.Bind("Hotkeys", "Group 3", new KeyboardShortcut(KeyCode.Alpha3), "Raise or lower SailMaster group 3.");
            group4SailsKey = Config.Bind("Hotkeys", "Group 4", new KeyboardShortcut(KeyCode.Alpha4), "Raise or lower SailMaster group 4.");
            group5SailsKey = Config.Bind("Hotkeys", "Group 5", new KeyboardShortcut(KeyCode.Alpha5), "Raise or lower SailMaster group 5.");
            group6SailsKey = Config.Bind("Hotkeys", "Group 6", new KeyboardShortcut(KeyCode.Alpha6), "Raise or lower SailMaster group 6.");
            toggleGuiKey = Config.Bind("Hotkeys", "Toggle GUI", new KeyboardShortcut(KeyCode.F7), "Show or hide the SailMaster control panel.");
            hoistingSpeed = Config.Bind("Behavior", "Hoisting Speed", 0.005f, "Amount the reefing rope changes per frame while raising or lowering.");
            autoTrimIntervalSeconds = Config.Bind("Behavior", "Auto Trim Interval Seconds", 0.05f, "Seconds between auto-trim updates per sail. Set to 0 to update every frame.");
            navigationKp = Config.Bind("Navigation", "PID Kp", 0.03f, "Proportional steering gain for heading lock and route following.");
            navigationKi = Config.Bind("Navigation", "PID Ki", 0.005f, "Integral steering gain for heading lock and route following.");
            navigationKd = Config.Bind("Navigation", "PID Kd", 0.015f, "Derivative steering gain for heading lock and route following.");
            showGuiOnStart = Config.Bind("GUI", "Show On Start", false, "Show the SailMaster control panel when the game starts.");
            timingProfilerLog = Config.Bind("Debug", "Timing Profiler Log", false, "Log aggregated SailMaster timing samples to the BepInEx log when enabled.");
            timingProfilerIntervalSeconds = Config.Bind("Debug", "Timing Profiler Interval Seconds", 5f, "Seconds between aggregated SailMaster timing reports.");
            simulatedSailRows = Config.Bind("Debug", "Simulated Sail Rows", 0, "Add non-interactive fake sail rows to the GUI for layout testing.");
            simulatedTrimControlsPerSail = Config.Bind("Debug", "Simulated Trim Controls Per Sail", 1, "Trim controls shown on each simulated sail row.");

            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            gui = gameObject.AddComponent<SailMasterGui>();
            gui.Visible = showGuiOnStart.Value;
            Logger.LogInfo("SailMaster loaded.");
        }

        private void Update()
        {
            SailMasterProfiler.MaybeReport(Time.unscaledTime);
        }
    }
}
