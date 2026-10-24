using System;
using System.Reflection;
using HarmonyLib;
using IsoRim;
using Verse;

namespace ProjectZ.Windows
{
    // IsoRim draws a wall block as tall as the ThingForm it computes for the def, and that form's height comes from
    // the ModelDef's <height> - but only for a ModelDef that carries a model file. The three half walls deliberately
    // have no model (they are blocks, not models), so in the current IsoRim build their <height> is skipped and the
    // block is drawn `fillPercent x wall height` tall instead:
    //   bare half wall  - fillPercent 0.55 -> ~0.91 (close enough to 0.825 to look right);
    //   glazed half wall - fillPercent 1    -> 1.65, a full wall, with the window buried inside it (user, 2026-10-24:
    //                      "带窗的墙怎么直接变成整个的墙了？").
    // The glazed defs cannot simply drop to fillPercent 0.55: they are what holds the roof and seals the room.
    // So pin the drawn height here, after IsoRim has computed the form. Same height the window's model sits on.
    [HarmonyPatch]
    internal static class HalfWallHeight
    {
        public const float Height = 0.825f;

        private static MethodBase target;

        public static bool Prepare()
        {
            try
            {
                target = AccessTools.Method("IsoRim.Forms:For", new[] { typeof(ThingDef) });
            }
            catch (Exception e)
            {
                target = null;
                Log.Warning("[ProjectZ] windows: IsoRim.Forms.For not found: " + e.Message);
            }
            if (target == null)
            {
                Log.Warning("[ProjectZ] windows: no IsoRim.Forms.For, half walls keep IsoRim's own block height");
            }
            else
            {
                Log.Message("[ProjectZ] windows: half walls pinned to " + Height + " cells (IsoRim.Forms.For)");
            }
            return target != null;
        }

        public static MethodBase TargetMethod()
        {
            return target;
        }

        // Forms.For caches its ThingForm per def, so this runs once per def and then just re-sets the same value.
        public static void Postfix(ThingDef def, ThingForm __result)
        {
            if (def != null && __result != null && __result.form == Form3D.Block && HalfWallState.IsHalfWall(def))
            {
                __result.height = Height;
            }
        }
    }
}
