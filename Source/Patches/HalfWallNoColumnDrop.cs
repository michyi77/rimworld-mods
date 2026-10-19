using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A wall on the floor above a half wall would otherwise be drawn down over the window.
    //
    // IsoRim draws a wall standing on a wall on the floor below as ONE wall through both storeys: its sides
    // reach down to the top of the block below (IsoRim.Floors.WallColumn: drop = floor height - the height of
    // the block below). That height is the block's DRAWN height, and a half wall is drawn 0.825 tall - so a
    // wall above it drops 0.86 cells, i.e. its front face comes down to 0.82 above the ground floor: exactly
    // over the window, which fills 0.825..1.65 of that cell. From the floor above, the window then reads as
    // the upper wall's planks (user, 2026-10-19: "被二楼木墙渲染的图案遮挡").
    //
    // Fix: no drop where the block below is a half wall. The upper wall then stops at its own floor; the
    // slab edge (0.12 thick, down to 1.61) covers the 0.03 joint, so nothing looks open.
    //
    // This is the second place ProjectZ reaches into IsoRim internals (see HalfWallNoCut for the first). If
    // IsoRim renames WallColumn the patch fails loudly at startup rather than silently doing nothing.
    [HarmonyPatch(typeof(IsoRim.Floors), nameof(IsoRim.Floors.WallColumn))]
    public static class HalfWallNoColumnDrop
    {
        public static void Postfix(Map map, IntVec3 c, ref float drop)
        {
            if (drop <= 0f || map == null || !c.IsValid)
            {
                return;
            }
            IntVec3 under = IsoRim.Floors.Stacked(map, c, -1);
            if (!under.IsValid || !under.InBounds(map))
            {
                return;
            }
            Building below = under.GetEdifice(map);
            if (below != null && HalfWallState.IsHalfWall(below.def))
            {
                drop = 0f;
            }
        }
    }
}
