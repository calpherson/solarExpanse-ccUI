using System;
using System.Reflection;
using Game.UI.Windows.Elements.PlanMissionElements.PMScheduleElements;
using HarmonyLib;
using Manager;
using UnityEngine;

namespace CcUi
{
    /// <summary>
    /// Shared formatting for relative dates ("IN 3 MONTHS 21 DAYS", "2 MONTHS 4 DAYS AGO", "TODAY"),
    /// driven by the [PlanMissionDates] DisplayMode / InText / AgoText / TodayText settings.
    /// </summary>
    internal static class RelativeDates
    {
        private const string UiCanvasName = "UI";
        private const string AbsoluteColor = "#707070"; // game colour for secondary text in rich strings

        // static string PMLabels.TimeSpanToString(TimeSpan): " 3 MONTHS 21 DAYS" in the game's style.
        // Called, never patched: the game calls it every frame elsewhere.
        private static readonly MethodInfo TimeSpanToStringMethod =
            AccessTools.Method(typeof(PMLabels), "TimeSpanToString", new[] { typeof(TimeSpan) });

        internal static bool Enabled => Plugin.DateMode.Value != DateDisplayMode.Absolute;

        /// <summary>The text to show for <paramref name="target"/>, given the game's own absolute text.</summary>
        internal static string Display(DateTime target, DateTime now, string absolute)
        {
            string relative = Relative(target, now);
            if (Plugin.DateMode.Value == DateDisplayMode.Relative || string.IsNullOrEmpty(absolute))
                return relative;
            return $"{relative} <color={AbsoluteColor}>({absolute})</color>";
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

        internal static DateTime? CurrentGameTime()
        {
            var tc = MonoBehaviourSingleton<TimeController>.Instance;
            return tc != null ? tc.CurrentTime : (DateTime?)null;
        }

        /// <summary>Whole game days since the start; changes exactly when the displayed durations can change.</summary>
        internal static int? CurrentGameDay()
        {
            var tc = MonoBehaviourSingleton<TimeController>.Instance;
            return tc != null ? tc.TotalDays : (int?)null;
        }

        /// <summary>
        /// True if <paramref name="c"/> sits under the on-screen "UI" canvas. The game keeps off-screen
        /// window copies (for example under GameManager, used by the AI) that must stay vanilla.
        /// </summary>
        internal static bool IsOnUiCanvas(Component c)
        {
            var canvases = c.GetComponentsInParent<Canvas>(true);
            return canvases.Length > 0 && canvases[0].rootCanvas != null && canvases[0].rootCanvas.name == UiCanvasName;
        }

        internal static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
