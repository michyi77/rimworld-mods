using HarmonyLib;
using IsoRim;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // IsoRim outlines each wall run in the game's art style: a dark rim on the cap, a line along the ground and
    // a line up each END of the run (WallBoxes.PrintBlock -> EndLines; OutlineWidth = 0.05 cells,
    // LineShade = 0.32 of the wall's colour). An end line is drawn where the block on that side of the face is
    // LOWER than the wall being drawn. So a full wall standing next to a half wall gets one on both faces that
    // touch it, and a joined half wall + wall reads as a black frame between them - while two walls of the same
    // height joined together show no line at all (user, 2026-10-19: "单独的半墙和普通墙都有黑框，而连接起来的
    // 半墙和普通墙之间这些框都没有，但是半墙和墙之间就有这些框").
    //
    // Fix: no end line on a face whose end cell holds a half wall, so the joint looks like any other wall joint.
    // Only the line is skipped: the taller wall's exposed side (the reveal of the opening above the half wall)
    // is still drawn, and no face is removed, so nothing can show through from another angle.
    //
    // The face order is IsoRim's: south, north, west, east, with FaceRight = east, west, south, north. The array
    // is read from IsoRim when it has the field (so a reorder is followed); the literal below is only a fallback.
    [HarmonyPatch(typeof(WallBoxes), "EndLines")]
    public static class HalfWallNoEndLine
    {
        private static readonly IntVec3[] FaceRight;

        static HalfWallNoEndLine()
        {
            try
            {
                FaceRight = AccessTools.StaticFieldRefAccess<IntVec3[]>(typeof(WallBoxes), "FaceRight");
            }
            catch (System.Exception)
            {
                FaceRight = new[] { IntVec3.East, IntVec3.West, IntVec3.South, IntVec3.North };
            }
        }

        public static bool Prefix(Map map, IntVec3 c, int f)
        {
            if (map == null || f < 0 || f >= FaceRight.Length || !c.IsValid)
            {
                return true;
            }
            IntVec3 right = FaceRight[f];
            return !HalfWallAt(map, c - right) && !HalfWallAt(map, c + right);
        }

        private static bool HalfWallAt(Map map, IntVec3 cell)
        {
            if (!cell.IsValid || !cell.InBounds(map))
            {
                return false;
            }
            Building b = cell.GetEdifice(map);
            return b != null && HalfWallState.IsHalfWall(b.def);
        }
    }
}
