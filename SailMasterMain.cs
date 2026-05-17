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
        internal static ConfigEntry<KeyboardShortcut> squareSailsKey;
        internal static ConfigEntry<KeyboardShortcut> lateenSailsKey;
        internal static ConfigEntry<KeyboardShortcut> junkSailsKey;
        internal static ConfigEntry<KeyboardShortcut> gaffSailsKey;
        internal static ConfigEntry<KeyboardShortcut> staysailSailsKey;
        internal static ConfigEntry<KeyboardShortcut> otherSailsKey;
        internal static ConfigEntry<float> hoistingSpeed;

        private void Awake()
        {
            Logger = base.Logger;

            allSailsKey = Config.Bind("Hotkeys", "All Sails", new KeyboardShortcut(KeyCode.Alpha0), "Raise or lower all controllable sails.");
            squareSailsKey = Config.Bind("Hotkeys", "Square Sails", new KeyboardShortcut(KeyCode.Alpha1), "Raise or lower square sails.");
            lateenSailsKey = Config.Bind("Hotkeys", "Lateen Sails", new KeyboardShortcut(KeyCode.Alpha2), "Raise or lower lateen sails.");
            junkSailsKey = Config.Bind("Hotkeys", "Junk Sails", new KeyboardShortcut(KeyCode.Alpha3), "Raise or lower junk sails.");
            gaffSailsKey = Config.Bind("Hotkeys", "Gaff Sails", new KeyboardShortcut(KeyCode.Alpha4), "Raise or lower gaff sails.");
            staysailSailsKey = Config.Bind("Hotkeys", "Staysails", new KeyboardShortcut(KeyCode.Alpha5), "Raise or lower staysails.");
            otherSailsKey = Config.Bind("Hotkeys", "Other Sails", new KeyboardShortcut(KeyCode.Alpha6), "Raise or lower uncategorized sails. This includes fin sails for now.");
            hoistingSpeed = Config.Bind("Behavior", "Hoisting Speed", 0.005f, "Amount the reefing rope changes per frame while raising or lowering.");

            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Logger.LogInfo("SailMaster loaded.");
        }

        private void Update()
        {
            if (allSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(null);
            }
            else if (squareSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(SailCategory.square);
            }
            else if (lateenSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(SailCategory.lateen);
            }
            else if (junkSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(SailCategory.junk);
            }
            else if (gaffSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(SailCategory.gaff);
            }
            else if (staysailSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(SailCategory.staysail);
            }
            else if (otherSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandGroup(SailCategory.other);
            }
        }
    }
}
