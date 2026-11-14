using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Info;
using Game.UI.Windows.Windows;
using HarmonyLib;
using Language;
using UnityEngine;

namespace CcUi
{
    /// <summary>
    /// Shows START DATE / ARRIVAL DATE in the SpaceCraft Info window's mission info as relative time.
    /// See ccUi/docs/spacecraft-info-dates-plan.md for the investigation behind every choice here.
    ///
    /// - SpaceCraftInfoWindow.SetupMissionInfo() builds the rows (UIWindowWithStats.ClearStats +
    ///   AddStat) into missionInfoStatsContainer, once per open or spacecraft switch. The rows are
    ///   new objects each time.
    /// - The game never rewrites them as time passes, but the clock can run while the window is
    ///   open. So the postfix records (row, date, original text), and Tick() re-renders the
    ///   recorded rows whenever the game day changes (the formatter has day resolution).
    /// - Date rows are recognised by title, compared against the game's own translated title
    ///   strings, so it works in any language.
    /// </summary>
    internal static class SpaceCraftInfoDatesPatch
    {
        private const string StartDateKeyField = "GameUIWindowsSpacecraftInfoWindowMissionInfoStartDateKey";
        private const string ArrivalDateKeyField = "GameUIWindowsSpacecraftInfoWindowMissionInfoArrivalDateKey";

        private sealed class TrackedRow
        {
            public UIObjectStatsRow Row;
            public bool IsLaunch;           // true: START DATE, false: ARRIVAL DATE
            public string OriginalText;
        }

        private static readonly List<TrackedRow> Tracked = new List<TrackedRow>();
        private static MissionInfo _trackedMission;
        private static int _lastRenderedDay = int.MinValue;
        private static bool _loggedNoMatch;
        private static bool _loggedMatch;

        private static readonly FieldInfo StartKeyField = AccessTools.Field(typeof(SpaceCraftInfoWindow), StartDateKeyField);
        private static readonly FieldInfo ArrivalKeyField = AccessTools.Field(typeof(SpaceCraftInfoWindow), ArrivalDateKeyField);

        [HarmonyPatch(typeof(SpaceCraftInfoWindow), "SetupMissionInfo")]
        private static class SetupMissionInfoPatch
        {
            /// <summary>
            /// Remember which rows existed before the rebuild. ClearStats destroys old rows, but Unity
            /// destroys at end of frame, so they're still children during our postfix.
            /// </summary>
            private static void Prefix(RectTransform ___missionInfoStatsContainer, out HashSet<int> __state)
            {
                __state = new HashSet<int>();
                if (___missionInfoStatsContainer == null) return;
                for (int i = 0; i < ___missionInfoStatsContainer.childCount; i++)
                    __state.Add(___missionInfoStatsContainer.GetChild(i).gameObject.GetInstanceID());
            }

            private static void Postfix(SpaceCraftInfoWindow __instance, RectTransform ___missionInfoStatsContainer,
                MissionInfo ___missionInfo, HashSet<int> __state)
            {
                try
                {
                    Tracked.Clear();
                    _trackedMission = null;
                    if (!Plugin.SpaceCraftInfoEnabled.Value || !RelativeDates.Enabled) return;
                    if (___missionInfo == null || ___missionInfoStatsContainer == null) return;
                    if (!RelativeDates.IsOnUiCanvas(__instance)) return;

                    string startTitle = Normalize(TitleFor(__instance, StartKeyField));
                    string arrivalTitle = Normalize(TitleFor(__instance, ArrivalKeyField));

                    for (int i = 0; i < ___missionInfoStatsContainer.childCount; i++)
                    {
                        var child = ___missionInfoStatsContainer.GetChild(i);
                        if (__state != null && __state.Contains(child.gameObject.GetInstanceID())) continue; // old, being destroyed
                        var row = child.GetComponent<UIObjectStatsRow>();
                        if (row == null) continue;

                        string title = Normalize(row.Title);
                        if (title.Length == 0) continue;
                        if (title == startTitle)
                            Tracked.Add(new TrackedRow { Row = row, IsLaunch = true, OriginalText = row.Value });
                        else if (title == arrivalTitle)
                            Tracked.Add(new TrackedRow { Row = row, IsLaunch = false, OriginalText = row.Value });
                    }

                    if (Tracked.Count == 0)
                    {
                        if (!_loggedNoMatch)
                        {
                            _loggedNoMatch = true;
                            Plugin.Log.LogWarning($"[Dates] SpaceCraftInfo: no date rows matched titles '{startTitle}' / '{arrivalTitle}'; leaving vanilla text.");
                        }
                        return;
                    }
                    if (!_loggedMatch)
                    {
                        _loggedMatch = true;
                        Plugin.Log.LogInfo($"[Dates] SpaceCraftInfo: tracking {Tracked.Count} date row(s) (titles '{startTitle}' / '{arrivalTitle}').");
                    }

                    _trackedMission = ___missionInfo;
                    RenderAll();
                }
                catch (Exception ex)
                {
                    Tracked.Clear();
                    Plugin.Log.LogError($"[Dates] SetupMissionInfo postfix failed; leaving vanilla text: {ex}");
                }
            }
        }

        /// <summary>Called every frame by the plugin driver. Re-renders only when the game day changes.</summary>
        internal static void Tick()
        {
            if (Tracked.Count == 0) return;
            var day = RelativeDates.CurrentGameDay();
            if (!day.HasValue || day.Value == _lastRenderedDay) return;
            RenderAll();
        }

        private static void RenderAll()
        {
            var now = RelativeDates.CurrentGameTime();
            if (!now.HasValue || _trackedMission == null) return;
            _lastRenderedDay = RelativeDates.CurrentGameDay() ?? int.MinValue;

            for (int i = Tracked.Count - 1; i >= 0; i--)
            {
                var t = Tracked[i];
                if (t.Row == null) { Tracked.RemoveAt(i); continue; } // destroyed by a rebuild or close
                DateTime target = t.IsLaunch ? _trackedMission.DateLaunch : _trackedMission.DateArrive;
                t.Row.Value = RelativeDates.Display(target, now.Value, t.OriginalText);
            }
        }

        /// <summary>The translated title for the key held in <paramref name="keyField"/> (instance, static or const).</summary>
        private static string TitleFor(SpaceCraftInfoWindow window, FieldInfo keyField)
        {
            if (keyField == null) return null;
            var key = keyField.GetValue(keyField.IsStatic ? null : window) as string;
            return string.IsNullOrEmpty(key) ? null : LEManager.Get(key);
        }

        /// <summary>Case-, whitespace- and trailing-colon-insensitive form, so "START DATE:" == "Start date".</summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Trim().TrimEnd(':').Trim().ToUpperInvariant();
        }
    }
}
