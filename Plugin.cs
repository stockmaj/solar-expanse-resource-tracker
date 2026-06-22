#nullable disable
using BepInEx;
using HarmonyLib;
using SolarExpanseResourceTracker.Patches;
using SolarExpanseResourceTracker.UI;

namespace SolarExpanseResourceTracker
{
    [BepInPlugin("com.mod.solarexpanse.resourcetracker", "ResourceTracker", "0.5.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }
        internal static ResourceTrackerConfig TrackerConfig { get; private set; }

        void Awake()
        {
            Log = base.Logger;
            TrackerConfig = new ResourceTrackerConfig(base.Config);
            var harmony = new Harmony("com.mod.solarexpanse.resourcetracker");
            harmony.PatchAll();
            PauseScreenEscPatch.Apply(harmony, Log);
            Log.LogInfo("ResourceTracker loaded");
        }
    }
}
