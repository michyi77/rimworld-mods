using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Mod settings for the daylight the windows and bare half walls let in (SPEC §30).
    public class WindowLightSettings : ModSettings
    {
        // In ticks now, 1..60, default 30 (half a second, the middle of the slider). The lowest setting really is every
        // tick - the light table is then rebuilt every tick, so the periodic pass (which also scans open roof edges)
        // costs a full map walk each tick (user, 2026-10-28).
        public int ticks = 30;        // how often the light is looked at
        public float step = 0.05f;    // how much the sky has to change before the grid is touched

        public static WindowLightSettings Current
        {
            get { return WindowsMod.Settings; }
        }

        public static int IntervalTicks
        {
            get { return Mathf.Clamp(Current == null ? 30 : Current.ticks, 1, 60); }
        }

        public static float Step
        {
            get { return Current == null ? 0.05f : Mathf.Max(0.01f, Current.step); }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref ticks, "ticks", 30);
            Scribe_Values.Look(ref step, "step", 0.05f);
        }
    }
}
