using System;
using System.Collections.Generic;
using Game.UI.Windows.Elements.PlanMissionElements.PMScheduleElements;
using Game.UI.Windows.Windows;
using HarmonyLib;

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

        private const string CyclicalWindowType = "PlanCyclicalMissionWindow";

        private static readonly Dictionary<int, Target> TargetCache = new Dictionary<int, Target>();
        private static readonly Dictionary<int, string> OriginalStartText = new Dictionary<int, string>();

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

                    if (!RelativeDates.Enabled) return;
                    var target = Classify(__instance);
                    if (target == Target.Skip) return;
                    if (target == Target.Cyclical && !Plugin.ApplyToCyclical.Value) return;

                    var now = RelativeDates.CurrentGameTime();
                    if (!now.HasValue) return;

                    if (___startDateValueTextTextMeshPro != null)
                    {
                        string original = ___startDateValueTextTextMeshPro.Value;
                        OriginalStartText[id] = original;
                        ___startDateValueTextTextMeshPro.Value = RelativeDates.Display(departureTimeDate, now.Value, original);
                    }
                    if (___arrivalDateValueTextTextMeshPro != null)
                    {
                        string original = ___arrivalDateValueTextTextMeshPro.Value;
                        ___arrivalDateValueTextTextMeshPro.Value = RelativeDates.Display(arrival, now.Value, original);
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

        /// <summary>Which PMLabels to touch: only those on the on-screen "UI" canvas. Cached per instance; logged once.</summary>
        private static Target Classify(PMLabels labels)
        {
            int id = labels.GetInstanceID();
            Target cached;
            if (TargetCache.TryGetValue(id, out cached)) return cached;

            Target target = Target.Skip;
            var window = labels.GetComponentInParent<UIWindow>(true);
            if (RelativeDates.IsOnUiCanvas(labels))
                target = window != null && window.GetType().Name == CyclicalWindowType ? Target.Cyclical : Target.PlanMission;

            TargetCache[id] = target;
            Plugin.Log.LogInfo($"[Dates] PMLabels at '{RelativeDates.PathOf(labels.transform)}' (window: {(window != null ? window.GetType().Name : "none")}) → {target}.");
            return target;
        }
    }
}
