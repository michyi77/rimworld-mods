using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Cutting a wall down to a half wall throws the wall's material away: the half wall is an edifice, so GenSpawn's
    // SpawningWipes() says it replaces whatever edifice is already there and the wall is destroyed with
    // DestroyMode.Vanish - no leavings, nothing returned. A player who drags a half wall over a standing wall is
    // literally removing material, so half of the wall's stuff is dropped back on the ground, the same share a
    // deconstruct gives (user, 2026-11-09, from the workshop comment "you spend 15 material… you are removing material
    // for it after all").
    //
    // Done on the wipe rather than on the build: the wipe is where the wall actually goes (the blueprint already wipes
    // it), and this way any path that removes it - blueprint, frame or finished building - gives the same share back.
    [HarmonyPatch]
    internal static class HalfWallRefund
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            target = AccessTools.Method(typeof(GenSpawn), "WipeExistingThings",
                new[] { typeof(IntVec3), typeof(Rot4), typeof(BuildableDef), typeof(Map), typeof(DestroyMode) });
            if (target == null)
            {
                Log.Warning("[ProjectZ] windows: GenSpawn.WipeExistingThings not found, a cut-down wall gives nothing back");
            }
            return target != null;
        }

        public static MethodBase TargetMethod()
        {
            return target;
        }

        public static void Prefix(IntVec3 thingPos, Rot4 thingRot, BuildableDef thingDef, Map map, DestroyMode mode)
        {
            if (map == null || thingDef == null || !IsOurHalfWall(thingDef))
            {
                return;
            }
            foreach (IntVec3 c in GenAdj.CellsOccupiedBy(thingPos, thingRot, thingDef.Size))
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                Building old = c.GetEdifice(map);
                if (old == null || old.def == null || !old.def.destroyable)
                {
                    continue;
                }
                if (HalfWallState.IsHalfWall(old.def))
                {
                    continue;   // its own kind: the def swap handles it, nothing is lost
                }
                if (!GenSpawn.SpawningWipes(thingDef, old.def))
                {
                    continue;   // this spawn will not take it away
                }
                Refund(old, map, c);
            }
        }

        // The def being spawned may be the half wall itself, or the blueprint/frame that precedes it - the blueprint is
        // what wipes a standing wall in the first place.
        private static bool IsOurHalfWall(BuildableDef def)
        {
            ThingDef t = def as ThingDef;
            if (t == null)
            {
                return false;
            }
            if (t.IsBlueprint || t.IsFrame)
            {
                t = t.entityDefToBuild as ThingDef;
            }
            return HalfWallState.IsHalfWall(t);
        }

        private static void Refund(Building old, Map map, IntVec3 c)
        {
            ThingDef stuff = old.Stuff;
            int full = old.def.costStuffCount;
            if (stuff == null || full <= 0)
            {
                return;
            }
            int back = Mathf.Max(1, Mathf.FloorToInt(full * 0.5f));
            Thing items = ThingMaker.MakeThing(stuff);
            if (items == null)
            {
                return;
            }
            items.stackCount = Mathf.Min(back, items.def.stackLimit);
            // The wall is still standing at this point, so the stack lands next to the cell, not on it.
            GenPlace.TryPlaceThing(items, c, map, ThingPlaceMode.Near);
        }
    }
}
