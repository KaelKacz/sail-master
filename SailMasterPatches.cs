using HarmonyLib;

namespace SailMaster
{
    [HarmonyPatch(typeof(Sail), "Start")]
    public class SailMasterPatches
    {
        private static void Postfix(Sail __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<SailMasterControlSail>() != null) return;

            __instance.gameObject.AddComponent<SailMasterControlSail>();
        }
    }

    [HarmonyPatch(typeof(Rudder), "Start")]
    public class SailMasterNavigationPatches
    {
        private static void Postfix(Rudder __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<SailMasterNavigationController>() != null) return;

            __instance.gameObject.AddComponent<SailMasterNavigationController>();
        }
    }

}
