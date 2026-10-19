using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A glazed half wall and its window are one thing to the player: take the wall down and the window goes with it,
    // instead of a window left floating in an empty cell.
    //
    // The window is destroyed inside the wall's own Destroy, and must not turn the wall back into a barricade while that
    // happens (Building_WindowGlass.Destroy would otherwise spawn a fresh bare wall where the player just deconstructed
    // one), so HalfWallState.WallGoingDown is raised around it.
    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    public static class GlazedWallTakesItsWindow
    {
        public static void Prefix(Building __instance)
        {
            if (HalfWallState.Swapping || HalfWallState.WallGoingDown)
            {
                return;   // our own swap of the wall's kind, not the player taking the wall down
            }
            if (__instance == null || __instance.def == null || !HalfWallState.IsGlazed(__instance.def) || !__instance.Spawned)
            {
                return;
            }
            Building_WindowGlass window = null;
            var things = __instance.Map.thingGrid.ThingsListAt(__instance.Position);
            for (int i = 0; i < things.Count; i++)
            {
                window = things[i] as Building_WindowGlass;
                if (window != null)
                {
                    break;
                }
            }
            if (window == null)
            {
                return;
            }
            HalfWallState.WallGoingDown = true;
            try
            {
                window.Destroy(DestroyMode.Vanish);
            }
            finally
            {
                HalfWallState.WallGoingDown = false;
            }
        }
    }
}
