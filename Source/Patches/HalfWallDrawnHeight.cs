using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace ProjectZ.Windows
{
    // The drawn height of a wall block: SectionLayer_IsoRimWalls asks WallBoxes.HeightFor(t, thingForm.height) and
    // hands the result straight to PrintBlock. Pinning the answer here keeps a half wall at 0.825 cells even if some
    // other IsoRim path hands in a taller figure - the glazed defs are fillPercent 1 (they hold the roof), so without
    // this their block is drawn a full wall tall and the window disappears inside it (SPEC §27).
    //
    // Found by name and skipped when missing, like the other IsoRim patches: a changed signature must not take the
    // rest of PatchAll down with it.
    [HarmonyPatch]
    internal static class HalfWallDrawnHeight
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            try
            {
                target = AccessTools.Method("IsoRim.WallBoxes:HeightFor", new[] { typeof(Thing), typeof(float) });
            }
            catch (Exception)
            {
                target = null;
            }
            return target != null;
        }

        public static MethodBase TargetMethod()
        {
            return target;
        }

        public static void Postfix(Thing t, ref float __result)
        {
            if (t != null && t.def != null && HalfWallState.IsHalfWall(t.def))
            {
                __result = HalfWallHeight.Height;
            }
        }
    }
}
