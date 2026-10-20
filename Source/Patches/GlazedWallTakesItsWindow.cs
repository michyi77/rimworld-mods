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
    //
    // It also has to hand back the window's material (user, 2026-10-20): the boards are broken out of the frame with the
    // wall, not taken apart carefully, so they are left the way the game leaves a *destroyed* window - a quarter of its
    // cost (GenLeaving.LeaveFraction_Kill 0.25) - and not what deconstructing the window itself gives back (half of it,
    // resourcesFractionWhenDeconstructed). Destroy(Vanish) gave nothing at all before.
    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    public static class GlazedWallTakesItsWindow
    {
        public static void Prefix(Building __instance, DestroyMode mode)
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
            // Taken down with the wall (deconstructed or destroyed): as if the glazing itself had been destroyed. Anything
            // else that removes the wall (Vanish, WillReplace) takes the window with it and leaves nothing.
            DestroyMode windowMode = mode == DestroyMode.Deconstruct || mode == DestroyMode.KillFinalize
                ? DestroyMode.KillFinalize
                : DestroyMode.Vanish;
            HalfWallState.WallGoingDown = true;
            try
            {
                window.Destroy(windowMode);
            }
            finally
            {
                HalfWallState.WallGoingDown = false;
            }
        }
    }
}
