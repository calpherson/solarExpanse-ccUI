using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

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
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<DateDisplayMode> DateMode;
        internal static ConfigEntry<bool> ApplyToCyclical;
        internal static ConfigEntry<string> InText;
        internal static ConfigEntry<string> AgoText;
        internal static ConfigEntry<string> TodayText;
        internal static ConfigEntry<bool> SpaceCraftInfoEnabled;
        internal static ConfigEntry<bool> RocketListArrivalEnabled;
        internal static ConfigEntry<string> ArrivesText;

        private void Awake()
        {
            Log = Logger;

            DateMode = Config.Bind("PlanMissionDates", "DisplayMode", DateDisplayMode.Both,
                "How START DATE / ARRIVAL DATE show dates, in the mission planner and the SpaceCraft Info window. " +
                "Both = relative + absolute, Relative = relative only, Absolute = vanilla.");
            ApplyToCyclical = Config.Bind("PlanMissionDates", "ApplyToCyclicalPlanner", true,
                "Also change the dates in the cyclical-mission planner (it uses the same rows).");
            InText = Config.Bind("PlanMissionDates", "InText", "IN",
                "Word placed before a future duration, as in \"IN 3 MONTHS 21 DAYS\".");
            AgoText = Config.Bind("PlanMissionDates", "AgoText", "AGO",
                "Word placed after a past duration, as in \"3 DAYS AGO\".");
            TodayText = Config.Bind("PlanMissionDates", "TodayText", "TODAY",
                "Shown when the date is less than a day away.");
            SpaceCraftInfoEnabled = Config.Bind("SpaceCraftInfoDates", "Enabled", true,
                "Show the mission START DATE / ARRIVAL DATE in the SpaceCraft Info window as relative time " +
                "(kept up to date as game days pass). Uses the [PlanMissionDates] display settings.");
            RocketListArrivalEnabled = Config.Bind("RocketListArrival", "Enabled", true,
                "In the mission planner's spacecraft list, show when an in-flight spacecraft arrives, in its " +
                "description line. Uses the [PlanMissionDates] display settings.");
            ArrivesText = Config.Bind("RocketListArrival", "ArrivesText", "ARRIVES",
                "Word placed before the arrival, as in \"ARRIVES IN 1 MONTHS 16 DAYS\".");

            // Per-frame work (the SpaceCraft Info day ticker) runs on our own persistent object, not
            // BepInEx's manager object, which some games destroy or disable.
            var go = new GameObject("CcUi.Driver");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<CcUiDriver>();

            try
            {
                new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{Name} {Version} loaded. Date display: {DateMode.Value}; SpaceCraft Info dates: {(SpaceCraftInfoEnabled.Value ? "on" : "off")}; rocket list arrival: {(RocketListArrivalEnabled.Value ? "on" : "off")}.");
            }
            catch (Exception ex)
            {
                Log.LogError($"Patching failed; dates stay vanilla: {ex}");
            }
        }
    }

    internal sealed class CcUiDriver : MonoBehaviour
    {
        private bool _tickFailedLogged;

        private void Update()
        {
            try
            {
                SpaceCraftInfoDatesPatch.Tick();
            }
            catch (Exception ex)
            {
                if (_tickFailedLogged) return;
                _tickFailedLogged = true;
                Plugin.Log.LogError($"[Dates] SpaceCraft Info tick failed (logged once): {ex}");
            }
        }
    }
}
