using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // The half wall under a window's blueprint is not an obstruction: the window is a second thing in the same cell,
    // so there is nothing to clear before building it.
    //
    // GenConstruct.BlocksConstruction is what makes a colonist treat a thing in the target cell as something to get rid
    // of first (GenConstruct.HandleBlockingThingJob deconstructs it), so a window ordered onto a half wall could have
    // the wall removed by the builder itself. Return false for that one pair.
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.BlocksConstruction))]
    public static class WindowWallDoesNotBlock
    {
        public static void Postfix(Thing constructible, Thing t, ref bool __result)
        {
            if (!__result || constructible == null || t == null || t.def == null)
            {
                return;
            }
            if (t.def.defName != "PZ_HalfWall")
            {
                return;
            }
            ThingDef built = BuiltDefOf(constructible);
            if (built != null && (built.defName == Building_WindowGlass.ClosedDefName || built.defName == Building_WindowGlass.OpenDefName))
            {
                __result = false;
            }
        }

        // What a blueprint or frame will become (the window), or the thing's own def.
        private static ThingDef BuiltDefOf(Thing t)
        {
            ThingDef def = t.def;
            return def != null && def.entityDefToBuild is ThingDef built ? built : def;
        }
    }
}
