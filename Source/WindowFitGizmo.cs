using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // The half wall's "Fit window" button, which orders the glazing as a blueprint for a colonist to build.
    //
    // It lives here, not in the base mod, so taking this mod out leaves the base untouched: a postfix on the game's own
    // Building.GetGizmos appends the button when the selected building is a half wall - found by def name, so there is
    // no reference to the base mod's assembly.
    [HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    public static class HalfWallFitWindowGizmo
    {
        private const string HalfWallDefName = "PZ_HalfWall";

        public static void Postfix(Thing __instance, ref IEnumerable<Gizmo> __result)
        {
            Building wall = __instance as Building;
            if (wall == null || wall.def == null || wall.def.defName != HalfWallDefName || wall.Map == null || __result == null)
            {
                return;
            }
            __result = __result.Concat(Buttons(wall));
        }

        private static IEnumerable<Gizmo> Buttons(Building wall)
        {
            if (WindowOrdered(wall))
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Fit window",
                defaultDesc = "Order glazing for this half wall: a colonist fits it, out of the wall's own material.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Vent"),
                action = () => FitWindow(wall)
            };
        }

        // A window in this cell already, or one planned (its blueprint, or the frame of it being built). The same test
        // the placement rule uses, so the button and the Structure menu agree on what "one window per half wall" means.
        private static bool WindowOrdered(Building wall)
        {
            return PlaceWorker_OnHalfWall.WindowInCell(wall.Position, wall.Map);
        }

        private static void FitWindow(Building wall)
        {
            Map map = wall.Map;
            if (map == null || WindowOrdered(wall))
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(Building_WindowGlass.ClosedDefName);
            if (def == null)
            {
                Log.Error("[ProjectZ] windows: def " + Building_WindowGlass.ClosedDefName + " is missing");
                return;
            }
            if (def.blueprintDef == null)
            {
                Log.Error("[ProjectZ] windows: " + def.defName + " has no blueprintDef; give it a designationCategory");
                return;
            }
            IntVec3 pos = wall.Position;
            Rot4 rot = wall.Rotation;
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(def, pos, rot, map);
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            // Same stuff as the half wall underneath, so the boards come out in the material's colour. The blueprint
            // asks the colonist to bring the window's cost in that stuff (costStuffCount in the def).
            GenConstruct.PlaceBlueprintForBuild(def, pos, map, rot, wall.Faction ?? Faction.OfPlayer, wall.Stuff);
        }
    }
}
