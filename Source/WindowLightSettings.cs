using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Mod settings for the daylight the windows and bare half walls let in (SPEC §30).
    public class WindowLightSettings : ModSettings
    {
        // 0.5 s: the light follows a window being slid open closely, and the work is throttled by the rebuild itself
        // (user, 2026-10-28: the default should be the shortest interval).
        public float seconds = 0.5f;   // how often the light is looked at
        public float step = 0.05f;     // how much the sky has to change before the grid is touched

        public static WindowLightSettings Current
        {
            get { return WindowsMod.Settings; }
        }

        public static int IntervalTicks
        {
            get { return Mathf.Max(1, Mathf.RoundToInt((Current == null ? 0.5f : Current.seconds) * 60f)); }
        }

        public static float Step
        {
            get { return Current == null ? 0.05f : Mathf.Max(0.01f, Current.step); }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref seconds, "seconds", 0.5f);
            Scribe_Values.Look(ref step, "step", 0.05f);
        }
    }
}
