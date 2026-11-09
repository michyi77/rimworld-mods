using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A window ordered straight onto a full wall: vanilla's CanPlaceBlueprintOver() refuses a non-edifice thing under a
    // building (BuildingProperties.canBuildNonEdificesUnder is false for walls), so the blueprint could never be laid on
    // the wall's own cell. The window is exactly the case where that is wanted - it is fitted INTO the wall, and the
    // wall is cut down to a half wall of its own stuff when the window is finished (HalfWallState.CutDown).
    //
    // Kept to "our window onto a wall this addon can cut down", so nothing else that cannot sit under a building gets
    // through.
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintOver))]
    internal static class WindowOnWallBlueprint
    {
        public static bool Prefix(BuildableDef newDef, ThingDef oldDef, ThingDef newStuff, ThingDef oldStuff,
            ref bool __result)
        {
            if (!PlaceWorker_OnHalfWall.IsWindowDef(newDef))
            {
                return true;
            }
            // CanPlaceBlueprintAt passes the wall's real stuff, so the material can be checked here as well; when it is
            // missing, settle for "it is a wall at all" and let the place worker say why not.
            ThingDef built = BuiltOf(oldDef);
            bool ok = oldStuff != null
                ? PlaceWorker_OnHalfWall.IsCuttableWallDef(built, oldStuff)
                : PlaceWorker_OnHalfWall.IsWallDef(built);
            if (!ok)
            {
                return true;
            }
            __result = true;
            return false;
        }

        private static ThingDef BuiltOf(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }
            ThingDef built = def.entityDefToBuild as ThingDef;
            return built ?? def;
        }
    }
}
