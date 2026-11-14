using System;
using System.Collections.Generic;
using System.Reflection;
using Game.UI.Windows.Elements.PlanMissionElements.PMScheduleElements;
using Game.UI.Windows.Windows;
using HarmonyLib;
using Manager;
using UnityEngine;

namespace CcUi
{
    /// <summary>
    /// Shows START DATE / ARRIVAL DATE in the mission planner as relative time.
    /// See ccUi/docs/relative-dates-plan.md for the investigation behind every choice here.
    ///
    /// - PMLabels.SetData(departure, arrival, span, refreshUI) is the only writer of these rows,
    ///   and runs only when the plan changes. The game stays paused while planning, so "now" is
    ///   frozen and the text never goes stale. We postfix it and rewrite the two rows.
    /// - Only PMLabels under the on-screen "UI" canvas are touched. The game also keeps
    ///   off-screen PlanMissionWindow copies under GameManager (one is PlanMissionWindowAI) that the
    ///   AI plans through; those stay vanilla.
    /// - Something reads the start-date text back (StartDateValueTextTextMeshProText). We don't
    ///   know what, so that getter keeps returning the game's original absolute text.
    /// </summary>
    internal static class PlanMissionDatesPatch
    {
        private enum Target { Skip, PlanMission, Cyclical }

        private const string UiCanvasName = "UI";
        private const string CyclicalWindowType = "PlanCyclicalMissionWindow";
        private const string UnitColor = "#707070"; // game colour for secondary text in rich strings

        private static readonly Dictionary<int, Target> TargetCache = new Dictionary<int, Target>();
        private static readonly Dictionary<int, string> OriginalStartText = new Dictionary<int, string>();

        // static string PMLabels.TimeSpanToString(TimeSpan): " 3 MONTHS 21 DAYS" in the game's style.
        // Called, never patched: the game calls it every frame elsewhere.
        private static readonly MethodInfo TimeSpanToStringMethod =
            AccessTools.Method(typeof(PMLabels), "TimeSpanToString", new[] { typeof(TimeSpan) });

        [HarmonyPatch(typeof(PMLabels), "SetData")]
        private static class SetDataPatch
        {
            private static void Postfix(PMLabels __instance, DateTime departureTimeDate, DateTime arrival,
                UIObjectStatsRow ___startDateValueTextTextMeshPro, UIObjectStatsRow ___arrivalDateValueTextTextMeshPro)
            {
                try
                {
                    int id = __instance.GetInstanceID();
                    OriginalStartText.Remove(id);

                    if (Plugin.DateMode.Value == DateDisplayMode.Absolute) return;
                    var target = Classify(__instance);
                    if (target == Target.Skip) return;
                    if (target == Target.Cyclical && !Plugin.ApplyToCyclical.Value) return;

                    var now = CurrentGameTime();
                    if (!now.HasValue) return;

                    if (___startDateValueTextTextMeshPro != null)
                    {
                        string original = ___startDateValueTextTextMeshPro.Value;
                        OriginalStartText[id] = original;
                        ___startDateValueTextTextMeshPro.Value = Display(departureTimeDate, now.Value, original);
                    }
                    if (___arrivalDateValueTextTextMeshPro != null)
                    {
                        string original = ___arrivalDateValueTextTextMeshPro.Value;
                        ___arrivalDateValueTextTextMeshPro.Value = Display(arrival, now.Value, original);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Dates] SetData postfix failed; leaving vanilla text: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(PMLabels), "StartDateValueTextTextMeshProText", MethodType.Getter)]
        private static class StartDateTextGetterPatch
        {
            private static void Postfix(PMLabels __instance, ref string __result)
            {
                string original;
                if (OriginalStartText.TryGetValue(__instance.GetInstanceID(), out original))
                    __result = original;
            }
        }

        private static string Display(DateTime target, DateTime now, string absolute)
        {
            string relative = Relative(target, now);
            if (Plugin.DateMode.Value == DateDisplayMode.Relative || string.IsNullOrEmpty(absolute))
                return relative;
            return $"{relative} <color={UnitColor}>({absolute})</color>";
        }

        internal static string Relative(DateTime target, DateTime now)
        {
            TimeSpan diff = target - now;
            if (Math.Abs(diff.TotalDays) < 1.0)
                return Plugin.TodayText.Value;

            bool future = diff > TimeSpan.Zero;
            string span = FormatSpan(future ? diff : now - target);
            return future
                ? $"{Plugin.InText.Value} {span}"
                : $"{span} {Plugin.AgoText.Value}";
        }

        /// <summary>The game's own duration text, with a plain fallback if it's unavailable or empty.</summary>
        private static string FormatSpan(TimeSpan span)
        {
            try
            {
                var text = TimeSpanToStringMethod?.Invoke(null, new object[] { span }) as string;
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Dates] TimeSpanToString failed, using fallback: {ex.Message}");
            }
            int days = (int)Math.Round(span.TotalDays);
            return days == 1 ? "1 DAY" : $"{days} DAYS";
        }

        private static DateTime? CurrentGameTime()
        {
            var tc = MonoBehaviourSingleton<TimeController>.Instance;
            return tc != null ? tc.CurrentTime : (DateTime?)null;
        }

        /// <summary>Which PMLabels to touch: only those on the on-screen "UI" canvas. Cached per instance; logged once.</summary>
        private static Target Classify(PMLabels labels)
        {
            int id = labels.GetInstanceID();
            Target cached;
            if (TargetCache.TryGetValue(id, out cached)) return cached;

            Target target = Target.Skip;
            var canvases = labels.GetComponentsInParent<Canvas>(true);
            bool onUiCanvas = canvases.Length > 0 && canvases[0].rootCanvas != null && canvases[0].rootCanvas.name == UiCanvasName;
            var window = labels.GetComponentInParent<UIWindow>(true);
            if (onUiCanvas)
                target = window != null && window.GetType().Name == CyclicalWindowType ? Target.Cyclical : Target.PlanMission;

            TargetCache[id] = target;
            Plugin.Log.LogInfo($"[Dates] PMLabels at '{PathOf(labels.transform)}' (window: {(window != null ? window.GetType().Name : "none")}) → {target}.");
            return target;
        }

        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
