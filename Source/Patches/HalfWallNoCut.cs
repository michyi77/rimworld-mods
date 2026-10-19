using HarmonyLib;
using IsoRim;
using Verse;

namespace ProjectZ.Windows
{
    // IsoRim cuts a wall down while the pointer is over it. A half wall is already short, so the cut
    // halves it again and it reads as a stub rather than a low wall.
    //
    // CanBeCut() is `Fill(def) >= 0.9f`, and the half wall needs fillPercent 1 so that ThingDef.Fillage
    // is Full and it splits rooms - so the flag cannot be avoided from the def. This is the one place
    // where ProjectZ reaches into IsoRim internals; if IsoRim renames CanBeCut the patch fails at
    // startup and the log says so, rather than silently doing nothing.
    [HarmonyPatch(typeof(WallBoxes), nameof(WallBoxes.CanBeCut))]
    public static class HalfWallNoCut
    {
        public static bool Prefix(ThingDef def, ref bool __result)
        {
            if (HalfWallState.IsHalfWall(def))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
