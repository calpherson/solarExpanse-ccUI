using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CcUi
{
    public enum DateDisplayMode
    {
        /// <summary>"IN 3 MONTHS 21 DAYS (2173 May 20)": relative, with the game's absolute date in grey.</summary>
        Both,
        /// <summary>"IN 3 MONTHS 21 DAYS"</summary>
        Relative,
        /// <summary>Vanilla: "2173 May 20".</summary>
        Absolute,
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.mod.solarexpanse.ccui";
        public const string Name = "ccUi";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<DateDisplayMode> DateMode;
        internal static ConfigEntry<bool> ApplyToCyclical;
        internal static ConfigEntry<string> InText;
        internal static ConfigEntry<string> AgoText;
        internal static ConfigEntry<string> TodayText;

        private void Awake()
        {
            Log = Logger;

            DateMode = Config.Bind("PlanMissionDates", "DisplayMode", DateDisplayMode.Both,
                "How the START DATE / ARRIVAL DATE rows in the mission planner show dates. " +
                "Both = relative + absolute, Relative = relative only, Absolute = vanilla.");
            ApplyToCyclical = Config.Bind("PlanMissionDates", "ApplyToCyclicalPlanner", true,
                "Also change the dates in the cyclical-mission planner (it uses the same rows).");
            InText = Config.Bind("PlanMissionDates", "InText", "IN",
                "Word placed before a future duration, as in \"IN 3 MONTHS 21 DAYS\".");
            AgoText = Config.Bind("PlanMissionDates", "AgoText", "AGO",
                "Word placed after a past duration, as in \"3 DAYS AGO\".");
            TodayText = Config.Bind("PlanMissionDates", "TodayText", "TODAY",
                "Shown when the date is less than a day away.");

            try
            {
                new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{Name} {Version} loaded. Plan-mission dates: {DateMode.Value}.");
            }
            catch (Exception ex)
            {
                Log.LogError($"Patching failed; plan-mission dates stay vanilla: {ex}");
            }
        }
    }
}
