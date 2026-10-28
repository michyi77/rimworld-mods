using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace ProjectZ.Windows
{
    // The bare half wall is fillPercent 0.55: that is the figure the game uses for its cover and for a shot passing over
    // the top, and it has to stay. But IsoRim also uses the same figure to decide what the cell *is* when it draws the
    // world - WallBoxes.Fill(def) < 0.9 means "not a full block", and Floors.FullBlock() then says the cell's ground is
    // still visible. So a half wall's own cell kept its bare terrain, and next to a wooden floor it read as a strip of
    // grass under the wall, which a full wall does not do (user, 2026-10-28).
    //
    // These two patches only change what IsoRim *draws*: the cell is treated as a full block (ground hidden, floor
    // handled like a wall's), while the def keeps its 0.55 fill for cover and the drawn height stays 0.825
    // (HalfWallHeight / HalfWallDrawnHeight). Nothing here touches the game's own figures.
    [HarmonyPatch]
    internal static class HalfWallFill
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            try
            {
                target = AccessTools.Method("IsoRim.WallBoxes:Fill", new[] { typeof(ThingDef) });
            }
            catch (Exception)
            {
                target = null;
            }
            if (target == null)
            {
                Log.Warning("[ProjectZ] windows: no IsoRim.WallBoxes.Fill, half walls keep their inset block");
            }
            return target != null;
        }

        public static MethodBase TargetMethod()
        {
            return target;
        }

        // Fill caches its answer per def, so the drawing code still asks every time and this is what it is told.
        public static void Postfix(ThingDef def, ref float __result)
        {
            if (def != null && __result < 1f && HalfWallState.IsHalfWall(def))
            {
                __result = 1f;
            }
        }
    }

    // Floors.FullBlock answers "is this cell filled by a block, so its ground does not show through": it wants
    // Fill >= 0.9 *and* LowWall(def) to be false. Half walls are deliberately low (0.825), so LowWall() says yes and the
    // cell counted as ground. The drawn block covers the whole cell either way; this only lets the ground layer treat it
    // like a wall's. CanBeCut() reaches its own answer from Fill, so cutting a low wall open for a pawn behind it still
    // works exactly as before.
    [HarmonyPatch]
    internal static class HalfWallLowWall
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            try
            {
                target = AccessTools.Method("IsoRim.WallBoxes:LowWall", new[] { typeof(ThingDef) });
            }
            catch (Exception)
            {
                target = null;
            }
            if (target == null)
            {
                Log.Warning("[ProjectZ] windows: no IsoRim.WallBoxes.LowWall, half walls keep their ground showing");
            }
            return target != null;
        }

        public static MethodBase TargetMethod()
        {
            return target;
        }

        public static void Postfix(ThingDef def, ref bool __result)
        {
            if (__result && def != null && HalfWallState.IsHalfWall(def))
            {
                __result = false;
            }
        }
    }
}
