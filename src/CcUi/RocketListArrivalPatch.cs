using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CcUi
{
    /// <summary>
    /// In the mission planner's SelectRocket list, shows when an in-flight spacecraft arrives
    /// ("ARRIVES IN 1 MONTHS 16 DAYS (2173 Apr 17)") in the entry's Description line.
    /// See ccUi/docs/rocket-list-mission-plan.md for the investigation behind every choice here.
    ///
    /// - Entries are UIRowRocket, bound through SetDataRowRocketData(StackedRowRocketData) and/or
    ///   SetData(ListElementData, UIWindow). We postfix both; applying twice is harmless.
    /// - The row never rewrites its own texts afterwards, and the game is paused while planning,
    ///   so writing once per bind is enough.
    /// - Data: StackedRowRocketData → RowRocketData.spacecraft (CustomUpdate.Spacecraft) →
    ///   currentPhase == Fly, trajectoryObject.EndDate = arrival.
    /// - Description is normally hidden; we show it only on rows we write, and restore the game's
    ///   text and visibility when a re-bound row is no longer in flight.
    /// </summary>
    internal static class RocketListArrivalPatch
    {
        private const string RowTypeName = "Game.UI.Windows.Elements.ObjectInfoElements.UIRowRocket";
        private const string PlannerWindowTypeName = "PlanMissionWindow";
        private const string FlyPhase = "Fly";

        private sealed class Saved
        {
            public string OriginalText;
            public bool OriginalActive;
            public string Written;
        }

        private static readonly Dictionary<int, Saved> SavedByRow = new Dictionary<int, Saved>();
        private static readonly HashSet<string> LoggedBinds = new HashSet<string>();
        private static bool _loggedFirstWrite;
        private static bool _loggedFailure;

        private static readonly Type RowType = AccessTools.TypeByName(RowTypeName);
        private static readonly FieldInfo DescriptionField = RowType != null ? AccessTools.Field(RowType, "rocketTypeTextMeshPro") : null;
        private static readonly FieldInfo StackField = RowType != null ? AccessTools.Field(RowType, "currentStackedRowRocketData") : null;
        private static readonly FieldInfo ParentWindowField = RowType != null ? AccessTools.Field(RowType, "parentWindow") : null;

        // Per runtime type, resolved lazily (RowRocketData, Spacecraft, TrajectoryObject).
        private static readonly Dictionary<string, MemberInfo> MemberCache = new Dictionary<string, MemberInfo>();

        [HarmonyPatch]
        private static class BindPatch
        {
            private static bool Prepare()
            {
                if (RowType == null || DescriptionField == null || StackField == null)
                {
                    Plugin.Log.LogWarning($"[Arrival] {RowTypeName} or its fields not found; rocket list arrival is off.");
                    return false;
                }
                return true;
            }

            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var name in new[] { "SetDataRowRocketData", "SetData" })
                {
                    var m = AccessTools.Method(RowType, name);
                    if (m != null) yield return m;
                    else Plugin.Log.LogWarning($"[Arrival] UIRowRocket.{name} not found.");
                }
            }

            private static void Postfix(Component __instance, MethodBase __originalMethod)
            {
                try
                {
                    Apply(__instance, __originalMethod.Name);
                }
                catch (Exception ex)
                {
                    if (_loggedFailure) return;
                    _loggedFailure = true;
                    Plugin.Log.LogError($"[Arrival] UIRowRocket postfix failed (logged once); leaving vanilla text: {ex}");
                }
            }
        }

        private static void Apply(Component row, string via)
        {
            if (!Plugin.RocketListArrivalEnabled.Value) return;
            if (!IsPlannerRow(row)) return;

            if (LoggedBinds.Add(via))
                Plugin.Log.LogInfo($"[Arrival] Planner rocket rows are bound via UIRowRocket.{via}.");

            var desc = DescriptionField.GetValue(row) as TMP_Text;
            if (desc == null) return;

            int id = row.GetInstanceID();
            Saved saved;
            SavedByRow.TryGetValue(id, out saved);
            // The game rewrote the description since our last write (row re-bound): that's its text now.
            if (saved != null && desc.text != saved.Written)
                saved.OriginalText = desc.text;

            DateTime? arrival = EarliestArrivalIfAllFlying(StackField.GetValue(row) as IEnumerable);
            var now = RelativeDates.CurrentGameTime();

            if (arrival.HasValue && now.HasValue)
            {
                if (saved == null)
                {
                    saved = new Saved { OriginalText = desc.text, OriginalActive = desc.gameObject.activeSelf };
                    SavedByRow[id] = saved;
                }
                if (!_loggedFirstWrite)
                {
                    _loggedFirstWrite = true;
                    Plugin.Log.LogInfo($"[Arrival] First in-flight row: Description was {(saved.OriginalActive ? "visible" : "hidden")}; arrival {arrival.Value:yyyy-MM-dd}.");
                }
                saved.Written = Format(arrival.Value, now.Value);
                desc.text = saved.Written;
                if (!desc.gameObject.activeSelf) desc.gameObject.SetActive(true);
            }
            else if (saved != null)
            {
                if (desc.text == saved.Written) desc.text = saved.OriginalText;
                if (desc.gameObject.activeSelf != saved.OriginalActive) desc.gameObject.SetActive(saved.OriginalActive);
                SavedByRow.Remove(id);
            }
        }

        private static string Format(DateTime arrival, DateTime now)
        {
            string absolute = arrival.ToString("yyyy MMM d", CultureInfo.InvariantCulture);
            string arrives = Plugin.ArrivesText.Value;
            if (Plugin.DateMode.Value == DateDisplayMode.Absolute)
                return $"{arrives} {absolute}";
            return $"{arrives} {RelativeDates.Display(arrival, now, absolute)}";
        }

        /// <summary>Only rows in the on-screen mission planner (not AI copies, not other windows' rocket lists).</summary>
        private static bool IsPlannerRow(Component row)
        {
            if (!RelativeDates.IsOnUiCanvas(row)) return false;
            var window = ParentWindowField?.GetValue(row) as Component;
            if (window == null) window = row.GetComponentInParent<Game.UI.Windows.Windows.UIWindow>(true);
            if (window == null) return false;
            for (var t = window.GetType(); t != null; t = t.BaseType)
                if (t.Name == PlannerWindowTypeName) return true;
            return false;
        }

        /// <summary>The earliest arrival in the stack, or null unless every spacecraft in it is flying.</summary>
        private static DateTime? EarliestArrivalIfAllFlying(IEnumerable stack)
        {
            if (stack == null) return null;
            DateTime? earliest = null;
            foreach (var rd in stack)
            {
                var sc = Get(rd, "spacecraft") as UnityEngine.Object;
                if (sc == null) return null; // fake/planned spacecraft: not in flight
                var phase = Get(sc, "currentPhase");
                if (phase == null || phase.ToString() != FlyPhase) return null;
                var traj = Get(sc, "trajectoryObject") as UnityEngine.Object;
                if (traj == null) return null;
                var end = Get(traj, "EndDate") ?? Get(traj, "endDate");
                if (!(end is DateTime)) return null;
                var date = (DateTime)end;
                if (!earliest.HasValue || date < earliest.Value) earliest = date;
            }
            return earliest;
        }

        /// <summary>Field or property value by name (cached per type), or null.</summary>
        private static object Get(object obj, string name)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            string key = type.FullName + "::" + name;
            MemberInfo member;
            if (!MemberCache.TryGetValue(key, out member))
            {
                member = (MemberInfo)AccessTools.Field(type, name) ?? AccessTools.Property(type, name);
                MemberCache[key] = member;
            }
            var f = member as FieldInfo;
            if (f != null) return f.GetValue(obj);
            var p = member as PropertyInfo;
            return p != null ? p.GetValue(obj, null) : null;
        }
    }
}
