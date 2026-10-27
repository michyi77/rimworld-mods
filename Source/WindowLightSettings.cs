using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Mod settings for the daylight the windows and bare half walls let in (SPEC §30).
    public class WindowLightSettings : ModSettings
    {
        public bool daylight = true;
        public float seconds = 3f;   // how often the light is looked at
        public float step = 0.05f;   // how much the sky has to change before the grid is touched

        public static WindowLightSettings Current
        {
            get { return WindowsMod.Settings; }
        }

        public static bool On
        {
            get { return Current == null || Current.daylight; }
        }

        public static int IntervalTicks
        {
            get { return Mathf.Max(1, Mathf.RoundToInt((Current == null ? 3f : Current.seconds) * 60f)); }
        }

        public static float Step
        {
            get { return Current == null ? 0.05f : Mathf.Max(0.01f, Current.step); }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref daylight, "daylight", true);
            Scribe_Values.Look(ref seconds, "seconds", 3f);
            Scribe_Values.Look(ref step, "step", 0.05f);
        }
    }
}
