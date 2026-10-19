using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A window is a second thing in the half wall's own cell, so it must never wipe what it is built on.
    //
    // GenSpawn.SpawningWipes tells the game whether a thing being spawned replaces what is already in the cell - that
    // is what lets a door or a vent be built over a wall, and it destroys the wall. Our window carries
    // canPlaceOverWall so its blueprint can be placed on the wall's occupied cell, and the game therefore treated it
    // as a wall-replacing thing: once the colonist finished it, the window stood there and the half wall was gone.
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    public static class WindowNoWallWipe
    {
        // One line in the log the first time it fires, so a report can tell whether this patch or the
        // blocks-construction one is doing the work.
        private static bool logged;

        public static void Postfix(BuildableDef newEntDef, ref bool __result)
        {
            if (!__result || newEntDef == null)
            {
                return;
            }
            if (newEntDef.defName == Building_WindowGlass.ClosedDefName || newEntDef.defName == Building_WindowGlass.OpenDefName)
            {
                __result = false;
                if (!logged)
                {
                    logged = true;
                    Log.Message("[ProjectZ] a window was spawned without wiping what it stands in");
                }
            }
        }
    }
}
