using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A window may be placed on a half wall, or straight onto a full wall - the "fit it into the wall" route (user,
    // 2026-11-09, from the workshop comment "should have them built into walls like over-wall vents, and for cheaper").
    //
    // On a full wall the blueprint sits in the wall's cell; when it is finished the window's own SpawnSetup cuts that
    // wall down into the bare half wall of the SAME stuff (HalfWallState.CutDown) and glazes it, so the player ends up
    // with the wall's material as a half wall plus the window they ordered - nothing is returned, the material is just
    // thinner.
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
            if (edifice == null || edifice.def == null)
            {
                return new AcceptanceReport("ProjectZ.NeedHalfWall".Translate());
            }
            if (!HalfWallState.IsHalfWall(edifice.def))
            {
                // A wall it cannot be cut from (smoothed rock, a modded wall with no usable stuff) gets its own message,
                // so the player knows the cell is the wrong kind rather than empty.
                if (edifice.def.Fillage != FillCategory.Full || edifice.def.building == null)
                {
                    return new AcceptanceReport("ProjectZ.NeedHalfWall".Translate());
                }
                if (!IsCuttableWall(edifice))
                {
                    return new AcceptanceReport("ProjectZ.WallNotCuttable".Translate());
                }
            }
            // `thing` is what is being placed (if anything): never let it count as the window already there, so a plan
            // that is being turned into a frame in its own cell is not refused by its own remains.
            if (WindowInCell(loc, map, thing ?? thingToIgnore))
            {
                return new AcceptanceReport("ProjectZ.HalfWallHasWindow".Translate());
            }
            return AcceptanceReport.WasAccepted;
        }

        // A standing full wall this addon can cut down: the half wall has to be buildable from the same stuff, or there
        // would be nothing to make it out of.
        public static bool IsCuttableWall(Thing t)
        {
            Building b = t as Building;
            return b != null && b.def != null && IsCuttableWallDef(b.def, b.Stuff);
        }

        // Any standing wall this addon may fit a window into, before the material question: the blueprint route only
        // gets the def, so the precise "can the half wall be made of this stuff" test is the place worker's job.
        public static bool IsWallDef(ThingDef def)
        {
            return def != null && !HalfWallState.IsHalfWall(def) && def.Fillage == FillCategory.Full
                && def.building != null;
        }

        public static bool IsCuttableWallDef(ThingDef def, ThingDef stuff = null)
        {
            if (!IsWallDef(def))
            {
                return false;
            }
            ThingDef bare = DefDatabase<ThingDef>.GetNamedSilentFail(HalfWallState.BareDefName);
            if (bare == null || bare.stuffCategories == null)
            {
                return false;
            }
            // With no stuff there is nothing to make a half wall out of (this is the smoothed-rock case).
            if (stuff == null)
            {
                return false;
            }
            List<StuffCategoryDef> cats = stuff.stuffProps == null ? null : stuff.stuffProps.categories;
            if (cats == null)
            {
                return false;
            }
            for (int i = 0; i < cats.Count; i++)
            {
                if (bare.stuffCategories.Contains(cats[i]))
                {
                    return true;
                }
            }
            return false;
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
