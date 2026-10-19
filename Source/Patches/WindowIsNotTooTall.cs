using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A window is flush with the wall it sits in, so a floor can go over it - but only the base mod (IsoRim ProjectZ) has
    // that slab height rule, and this addon is published on its own. So the exemption is applied only when that mod is
    // installed: no reference to its assembly, the rule is looked up by name from WindowsMod's constructor, and when it
    // is missing nothing happens.
    //
    // The rule (ProjectZ.HeightRule.TallThingAt -> TooTall -> IsoRim model height) refuses a floor over anything drawn
    // taller than a storey's clearance. A window's model is the frame plus the boards at the wall's own height, which is
    // over that clearance, so the rule flags it ("Wooden window below is too tall"); this clears the window, and the
    // glazed half wall under it, out of that answer.
    public static class WindowIsNotTooTall
    {
        public static void TryPatch(Harmony harmony)
        {
            System.Type rule = AccessTools.TypeByName("ProjectZ.HeightRule");
            MethodInfo target = rule == null ? null : AccessTools.Method(rule, "TallThingAt");
            if (target == null)
            {
                return;   // IsoRim ProjectZ is not installed: nothing to exempt from
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(WindowIsNotTooTall), nameof(Postfix)));
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[ProjectZ] windows: could not exempt windows from IsoRim ProjectZ's height rule: " + e, 0x5A17C6);
            }
        }

        public static void Postfix(ref Thing __result)
        {
            if (__result == null)
            {
                return;
            }
            if (__result is Building_WindowGlass)
            {
                __result = null;
                return;
            }
            if (__result.def != null && HalfWallState.IsGlazed(__result.def))
            {
                // The wall under the window: also flush with the storey, drawn half height.
                __result = null;
            }
        }
    }
}
