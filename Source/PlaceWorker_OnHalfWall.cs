using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A window may only be placed on a half wall, and a half wall takes ONE window.
    //
    // The window takes the half wall's cell: the half wall is what makes a wall "cut down", and the window is the
    // glazed version of that. So the cell has to hold a PZ_HalfWall first.
    // building.canPlaceOverWall on the window def lets the blueprint sit on the occupied cell; the
    // GenSpawn.SpawningWipes patch in this mod keeps the wall when the window is finished.
    //
    // One window per half wall (user, 2026-10-20): two windows in a cell - the second one placed from the Structure menu
    // with the same key, or turned a quarter - each draw their own frame and sash pair, so the frames cross over each
    // other ("cross-shaped window frame"). The window is a 1x1 thing in a 1x1 cell: there is no room for a second one,
    // whichever way it is turned, so any window already in the cell - built, or a blueprint, or a frame going up -
    // refuses the next one.
    public class PlaceWorker_OnHalfWall : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            Thing edifice = loc.GetEdifice(map);
            if (edifice == null || !HalfWallState.IsHalfWall(edifice.def))
            {
                return new AcceptanceReport("ProjectZ.NeedHalfWall".Translate());
            }
            // `thing` is what is being placed (if anything): never let it count as the window already there, so a plan
            // that is being turned into a frame in its own cell is not refused by its own remains.
            if (WindowInCell(loc, map, thing ?? thingToIgnore))
            {
                return new AcceptanceReport("ProjectZ.HalfWallHasWindow".Translate());
            }
            return AcceptanceReport.WasAccepted;
        }

        // A window in this cell already, or one planned: its blueprint, or the frame it is being built from. Both window
        // defs count, and the plan defs are resolved through entityDefToBuild, which is what a Blueprint or Frame def
        // carries instead of being the window itself.
        public static bool WindowInCell(IntVec3 loc, Map map, Thing ignore = null)
        {
            if (map == null || !loc.InBounds(map))
            {
                return false;
            }
            List<Thing> here = map.thingGrid.ThingsListAt(loc);
            for (int i = 0; i < here.Count; i++)
            {
                Thing t = here[i];
                if (t.def == null || t == ignore)
                {
                    continue;
                }
                BuildableDef built = t is Blueprint || t is Frame ? t.def.entityDefToBuild : t.def;
                if (IsWindowDef(built))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsWindowDef(BuildableDef def)
        {
            return def != null && (def.defName == Building_WindowGlass.ClosedDefName
                || def.defName == Building_WindowGlass.OpenDefName);
        }
    }
}
