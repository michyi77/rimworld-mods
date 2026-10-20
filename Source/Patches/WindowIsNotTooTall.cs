using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // The slab height rule belongs to IsoRim ProjectZ, which is not this addon's to change (and is downloaded from
    // elsewhere), so everything a window needs from it is done from this side. Two halves of that rule matter:
    //
    //   ProjectZ.HeightRule.TallThingAt -> "is something in this cell too tall for a floor over it?"  (a window is flush
    //   with the wall, so it must not count: it would refuse a floor over a fitted window)
    //
    //   ProjectZ.HeightRule.TooTall     -> "is this thing too tall to be placed under a floor?"        (the other
    //   direction: a window ordered under an upper floor is refused as "too tall for the floor above")
    //
    // Both are looked up by name only - no reference to that assembly - so with the base mod missing nothing happens.
    // TooTall is handed the plan def by the game when placing from the architect menu (a Blueprint or a Frame), so the
    // window's own def has to be read out of entityDefToBuild first.
    public static class WindowIsNotTooTall
    {
        public static void TryPatch(Harmony harmony)
        {
            System.Type rule = AccessTools.TypeByName("ProjectZ.HeightRule");
            if (rule == null)
            {
                return;   // IsoRim ProjectZ is not installed: nothing to exempt from
            }
            Patch(harmony, AccessTools.Method(rule, "TallThingAt"), nameof(TallThingAtPostfix), 0x5A17D0);
            Patch(harmony, AccessTools.Method(rule, "TooTall", new[] { typeof(BuildableDef) }), nameof(TooTallPostfix), 0x5A17D1);
        }

        private static void Patch(Harmony harmony, MethodInfo target, string postfix, int errorKey)
        {
            if (target == null)
            {
                return;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(WindowIsNotTooTall), postfix));
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[ProjectZ] windows: could not exempt windows from " + target.Name
                    + " in IsoRim ProjectZ's height rule: " + e, errorKey);
            }
        }

        // A floor over a cell holding a window or the glazed wall it stands in: neither is too tall.
        public static void TallThingAtPostfix(ref Thing __result)
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

        // A window (or its plan) ordered under a floor: it is flush with the wall, so the rule must not count it.
        public static void TooTallPostfix(BuildableDef def, ref bool __result)
        {
            if (!__result || def == null)
            {
                return;
            }
            ThingDef t = def as ThingDef;
            if (t == null)
            {
                return;
            }
            if (t.IsBlueprint || t.IsFrame)
            {
                ThingDef built = t.entityDefToBuild as ThingDef;
                if (built != null)
                {
                    t = built;
                }
            }
            if (t.defName == Building_WindowGlass.ClosedDefName || t.defName == Building_WindowGlass.OpenDefName)
            {
                __result = false;
            }
        }
    }
}
