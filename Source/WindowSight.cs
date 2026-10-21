using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Shooting through a window (user, 2026-10-21): while the boards are fully apart the cell has to count as see-through,
    // so a colonist inside can fire out and one outside can fire in. Nothing else is needed for that - a shot does not
    // collide with walls along its way - and the same question ("can this cell be seen over?") is what explosions, beam
    // weapons and the AI's target finding ask, which the player wanted as well.
    //
    // A window that is not fully open stays a wall: a cell is only registered once openPct reaches its end, so the answer
    // changes exactly when the boards finish sliding.
    //
    // The half wall under the window is what the shooter hides behind. Vanilla cover already reads the wall in the cell
    // next to the target; with the boards apart that wall is waist-high, so it is brought from a wall's 0.75 down to a
    // barricade's 0.55.
    //
    // Three patches, each applied on its own (a patch that cannot find its target costs only itself):
    //   1. GenGrid.CanBeSeenOverFast - the one question every sight test asks, vanilla and IsoRim ProjectZ's 3D sight
    //      alike (LosColumn.Block calls it).
    //   2. GenSight.LineOfSight - the walk behind every shot on one floor. CanBeSeenOverFast is a two-line method the
    //      JIT is free to inline (and an inlined copy cannot see a patch), so when the game's walk says "no" the line is
    //      walked again here with open windows let through.
    //   3. CoverUtility.TryFindAdjustedCoverInCell - the one place cover is worked out per neighbouring cell of the
    //      target, which both the shot and its report use.
    public static class WindowSight
    {
        // The cells (per map) whose window lets a shot through.
        private static readonly Dictionary<Map, HashSet<int>> openCells = new Dictionary<Map, HashSet<int>>();

        private static int openCount;

        private static bool logged;

        // How far open a normal window has to be for a shot to go through, and how far for it to be a clean shot (no
        // penalty). A sideways window always shoots through, always with the penalty: the wall it is set across leaves
        // only a slot to fire through (user, 2026-10-21).
        private const float ShootFromPct = 0.5f;

        private const float CleanPct = 0.999f;

        // The share of shots a slot takes: the window 50-99% open, or one set across the wall, stops this many and takes
        // the damage itself.
        private const float SlotCover = 0.3f;

        // A window set across the wall is always a slot and takes more of the shots than a normal one (user, 2026-10-21).
        private const float SidewaysCover = 0.5f;

        // Below half open there is only a crack to fire through: it still lets a shot out, but nine in ten hit the
        // window (user, 2026-10-21). At or below ShutPct the window is a wall again.
        private const float CrackCover = 0.9f;

        private const float ShutPct = 0.001f;

        public static bool AnyOpen
        {
            get { return openCount > 0; }
        }

        public static void TryPatch(Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(GenGrid), "CanBeSeenOverFast", new[] { typeof(IntVec3), typeof(Map) }),
                nameof(CanBeSeenOverPostfix), 0x5A17E0);
            Patch(harmony, AccessTools.Method(typeof(GenSight), "LineOfSight", new[]
            {
                typeof(IntVec3), typeof(IntVec3), typeof(Map), typeof(bool), typeof(Func<IntVec3, bool>), typeof(int), typeof(int)
            }), nameof(LineOfSightPostfix), 0x5A17E1);
            Patch(harmony, AccessTools.Method(typeof(CoverUtility), "TryFindAdjustedCoverInCell"),
                nameof(CoverPostfix), 0x5A17E2);
            Patch(harmony, AccessTools.Method(typeof(ShotReport), "HitReportFor",
                new[] { typeof(Thing), typeof(Verb), typeof(LocalTargetInfo) }), nameof(HitReportPostfix), 0x5A17E3);
            Patch(harmony, AccessTools.Method(typeof(CoverUtility), "ThingCovered", new[] { typeof(Thing), typeof(Map) }),
                nameof(ThingCoveredPrefix), 0x5A17E5, true);
            Patch(harmony, AccessTools.Method(typeof(Projectile), "CanHit", new[] { typeof(Thing) }),
                nameof(CanHitPostfix), 0x5A17E6);
            Patch(harmony, AccessTools.Method(typeof(ShotReport), "GetRandomCoverToMissInto"),
                nameof(GetCoverPostfix), 0x5A17E7);
            Patch(harmony, AccessTools.Method(typeof(Bullet), "Impact", new[] { typeof(Thing), typeof(bool) }),
                nameof(BulletImpactPrefix), 0x5A17E8, true);
        }

        private static void Patch(Harmony harmony, MethodInfo target, string postfix, int errorKey, bool prefix = false)
        {
            if (target == null)
            {
                Log.ErrorOnce("[ProjectZ] windows: a sight hook is missing; shooting through windows will not work ("
                    + postfix + ")", errorKey);
                return;
            }
            try
            {
                MethodInfo patch = AccessTools.Method(typeof(WindowSight), postfix);
                harmony.Patch(target, prefix: prefix ? new HarmonyMethod(patch) : null,
                    postfix: prefix ? null : new HarmonyMethod(patch));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[ProjectZ] windows: could not hook " + target.Name + " for window shooting: " + e, errorKey);
            }
        }

        // The base mod forbids sight between floors (VoidRule is a prefix on the very line-of-sight method we complete),
        // and a window in one floor's wall must not open a line into another band. SameBand is that mod's own test, called
        // by name so this addon still works when it is older than that rule.
        private static MethodInfo sameBand;

        private static bool sameBandTried;

        private static bool SameBand(IntVec3 start, IntVec3 end, Map map)
        {
            if (!sameBandTried)
            {
                sameBandTried = true;
                System.Type projectZ = AccessTools.TypeByName("ProjectZ.ProjectZ");
                sameBand = projectZ == null
                    ? null
                    : AccessTools.Method(projectZ, "SameBand", new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map) });
            }
            if (sameBand == null)
            {
                return true;
            }
            try
            {
                return (bool)sameBand.Invoke(null, new object[] { start, end, map });
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static bool OpenAt(IntVec3 c, Map map)
        {
            if (openCount <= 0 || map == null || !c.InBounds(map))
            {
                return false;
            }
            HashSet<int> cells;
            if (!openCells.TryGetValue(map, out cells))
            {
                return false;
            }
            return cells.Contains(map.cellIndices.CellToIndex(c));
        }

        // Called whenever the boards move (and on spawn and after a load): registers or unregisters the cell.
        public static void Sync(Building_WindowGlass window)
        {
            if (window == null || !window.Spawned)
            {
                return;
            }
            CompWindowLatch latch = window.Latch;
            float openPct = latch == null ? 0f : latch.openPct;
            // A sideways window always lets a shot through; a normal one as soon as it is open at all - through a crack it
            // is nearly all penalty (HitBlockChance), and only a window shut tight is a wall.
            bool open = WindowOrientation.IsSideways(window) || openPct > ShutPct;
            Map map = window.Map;
            if (map == null)
            {
                return;
            }
            HashSet<int> cells;
            int index = map.cellIndices.CellToIndex(window.Position);
            if (!open)
            {
                if (openCells.TryGetValue(map, out cells) && cells.Contains(index) && !AnotherOpenWindow(map, index, window))
                {
                    cells.Remove(index);
                    openCount--;
                    if (cells.Count == 0)
                    {
                        openCells.Remove(map);
                    }
                }
                return;
            }
            if (!openCells.TryGetValue(map, out cells))
            {
                cells = new HashSet<int>();
                openCells[map] = cells;
            }
            if (cells.Add(index))
            {
                openCount++;
            }
        }

        // The window is going away: its cell stops being see-through.
        public static void Forget(Building_WindowGlass window, Map map)
        {
            if (window == null || map == null)
            {
                return;
            }
            HashSet<int> cells;
            int index = map.cellIndices.CellToIndex(window.Position);
            if (openCells.TryGetValue(map, out cells) && cells.Contains(index))
            {
                cells.Remove(index);
                openCount--;
                if (cells.Count == 0)
                {
                    openCells.Remove(map);
                }
            }
        }

        private static bool AnotherOpenWindow(Map map, int index, Building_WindowGlass except)        {
            IntVec3 cell = map.cellIndices.IndexToCell(index);
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Building_WindowGlass other = things[i] as Building_WindowGlass;
                if (other == null || other == except)
                {
                    continue;
                }
                if (other.Latch != null && other.Latch.openPct >= 0.999f)
                {
                    return true;
                }
            }
            return false;
        }

        // The window in this cell, if any (a cell takes one window at most, but old saves may hold two).
        private static Building_WindowGlass WindowAt(IntVec3 c, Map map)
        {
            List<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                Building_WindowGlass window = things[i] as Building_WindowGlass;
                if (window != null)
                {
                    return window;
                }
            }
            return null;
        }

        // The share of shots crossing this cell that the window itself stops: none for a window wide open (the player's
        // rule: 100% is a clean shot), SlotCover for one 50-99% open or set across the wall. The bullet lands on the
        // window and damages it (user, 2026-10-21).
        public static float HitBlockChance(IntVec3 c, Map map)
        {
            if (!OpenAt(c, map))
            {
                return 0f;
            }
            Building_WindowGlass window = WindowAt(c, map);
            if (window == null)
            {
                return 0f;
            }
            if (WindowOrientation.IsSideways(window))
            {
                return SidewaysCover;
            }
            CompWindowLatch latch = window.Latch;
            float openPct = latch == null ? 0f : latch.openPct;
            if (openPct >= CleanPct)
            {
                return 0f;              // wide open: a clean shot
            }
            return openPct >= ShootFromPct ? SlotCover : CrackCover;
        }

        // The same walk GenSight uses: the first window the line crosses (the one that takes the hit) and the share of
        // shots all such windows stop together.
        private static Building_WindowGlass BlockingWindow(IntVec3 start, IntVec3 end, Map map, out float block)
        {
            block = 0f;
            if (!start.InBounds(map) || !end.InBounds(map))
            {
                return null;
            }
            Building_WindowGlass first = null;
            int num = Mathf.Abs(end.x - start.x);
            int num2 = Mathf.Abs(end.z - start.z);
            int num3 = start.x;
            int num4 = start.z;
            int num5 = 1 + num + num2;
            int num6 = end.x > start.x ? 1 : -1;
            int num7 = end.z > start.z ? 1 : -1;
            num *= 4;
            num2 *= 4;
            int num8 = num / 2 - num2 / 2;
            IntVec3 c = default(IntVec3);
            while (num5 > 0)
            {
                c.x = num3;
                c.z = num4;
                float cellBlock = HitBlockChance(c, map);
                if (cellBlock > 0f)
                {
                    if (first == null)
                    {
                        first = WindowAt(c, map);
                    }
                    block = 1f - (1f - block) * (1f - cellBlock);
                }
                if (num5 <= 1)
                {
                    break;
                }
                if (num8 > 0 || (num8 == 0 && (start.x != end.x ? start.x < end.x : start.z < end.z)))
                {
                    num3 += num6;
                    num8 -= num2;
                }
                else
                {
                    num4 += num7;
                    num8 += num;
                }
                num5--;
            }
            return first;
        }

        // Patch 1: "can this cell be seen over?" - false means a wall.
        public static void CanBeSeenOverPostfix(IntVec3 c, Map map, ref bool __result)
        {
            if (__result || !AnyOpen || !OpenAt(c, map))
            {
                return;
            }
            __result = true;
            if (!logged)
            {
                logged = true;
                Log.Message("[ProjectZ] windows: sight now passes through an open window");
            }
        }

        // Patch 2: the game's own walk, redone with open windows let through. Only when it said "no".
        public static void LineOfSightPostfix(IntVec3 start, IntVec3 end, Map map, bool skipFirstCell,
            Func<IntVec3, bool> validator, int halfXOffset, int halfZOffset, ref bool __result)
        {
            if (__result || !AnyOpen || map == null)
            {
                return;
            }
            if (!Walk(start, end, map, skipFirstCell, validator, halfXOffset, halfZOffset))
            {
                return;
            }
            // Only a line inside one floor may be opened this way; between floors the base mod's rule stands.
            if (SameBand(start, end, map))
            {
                __result = true;
            }
        }

        // GenSight.LineOfSight (1.7) step by step: 1 + dx + dz cells, the game's own corner rule; the only change is that
        // a cell holding a fully open window does not stop the line.
        private static bool Walk(IntVec3 start, IntVec3 end, Map map, bool skipFirstCell,
            Func<IntVec3, bool> validator, int halfXOffset, int halfZOffset)
        {
            if (!start.InBounds(map) || !end.InBounds(map))
            {
                return false;
            }
            bool flag = start.x != end.x ? start.x < end.x : start.z < end.z;
            int num = Mathf.Abs(end.x - start.x);
            int num2 = Mathf.Abs(end.z - start.z);
            int num3 = start.x;
            int num4 = start.z;
            int num5 = 1 + num + num2;
            int num6 = end.x > start.x ? 1 : -1;
            int num7 = end.z > start.z ? 1 : -1;
            num *= 4;
            num2 *= 4;
            num += halfXOffset * 2;
            num2 += halfZOffset * 2;
            int num8 = num / 2 - num2 / 2;
            IntVec3 c = default(IntVec3);
            while (num5 > 1)
            {
                c.x = num3;
                c.z = num4;
                if (!skipFirstCell || c != start)
                {
                    if (!c.CanBeSeenOverFast(map) && !OpenAt(c, map))
                    {
                        return false;
                    }
                    if (validator != null && !validator(c))
                    {
                        return false;
                    }
                }
                if (num8 > 0 || (num8 == 0 && flag))
                {
                    num3 += num6;
                    num8 -= num2;
                }
                else
                {
                    num4 += num7;
                    num8 += num;
                }
                num5--;
            }
            return true;
        }

        // Patch 3: the wall under an open window covers like a barricade (0.55), not like a full wall (0.75); the ratio
        // keeps the game's own angle adjustments.
        private const float WallCover = 0.75f;

        private const float BarricadeCover = 0.55f;

        public static void CoverPostfix(IntVec3 adjCell, Map map, ref CoverInfo result)
        {
            if (result.BlockChance <= 0f || !AnyOpen || map == null)
            {
                return;
            }
            Building edifice = adjCell.GetEdifice(map);
            if (edifice == null || !HalfWallState.IsGlazed(edifice.def) || !OpenAt(adjCell, map))
            {
                return;
            }
            result = new CoverInfo(result.Thing, result.BlockChance * (BarricadeCover / WallCover));
        }

        // Patch 4: a shot that crosses a window that is only part open (or set across the wall) can be stopped by the
        // window itself: the boards, or the frame of a window set across the wall, are in the way. The game's cover
        // machinery is what turns a stopped shot into a projectile aimed at a thing, so the window is added to the shot's
        // own cover list - it then takes the hit and is damaged like any other cover (user, 2026-10-21). A window wide
        // open stops nothing, per the player's rule that 100% is a clean shot.
        private static FieldInfo coversField;

        private static FieldInfo coverChanceField;




        private static bool fieldsTried;

        public static void HitReportPostfix(Thing caster, LocalTargetInfo target, ref ShotReport __result)
        {
            if (!AnyOpen || caster == null)
            {
                return;
            }
            Map map = caster.Map;
            if (map == null)
            {
                return;
            }
            if (!SameBand(caster.Position, target.Cell, map))
            {
                return;   // between floors: the base mod works the shot report out itself
            }
            float block;
            Building_WindowGlass window = BlockingWindow(caster.Position, target.Cell, map, out block);
            if (window == null || block <= 0.001f)
            {
                return;
            }
            if (!Fields())
            {
                return;
            }
            object boxed = __result;
            List<CoverInfo> covers = coversField.GetValue(boxed) as List<CoverInfo>;
            if (covers == null)
            {
                covers = new List<CoverInfo>();
                coversField.SetValue(boxed, covers);
            }
            covers.Add(new CoverInfo(window, block));
            float oldChance = (float)coverChanceField.GetValue(boxed);
            coverChanceField.SetValue(boxed, 1f - (1f - oldChance) * (1f - block));
            __result = (ShotReport)boxed;


        }

        private static bool Fields()
        {
            if (!fieldsTried)
            {
                fieldsTried = true;
                coversField = AccessTools.Field(typeof(ShotReport), "covers");
                coverChanceField = AccessTools.Field(typeof(ShotReport), "coversOverallBlockChance");
                if (coversField == null || coverChanceField == null)
                {
                    Log.ErrorOnce("[ProjectZ] windows: ShotReport's cover fields not found; shots through a half-open "
                        + "window will not hit the window", 0x5A17E5);
                }
            }
            return coversField != null && coverChanceField != null;
        }

        // Patch 5: a window stands in a wall, so the game's "is this thing shielded by a full-fill thing in its cell?"
        // answers yes about it, and a projectile aimed at it could never land. The window is exactly what a shot through
        // the opening reaches, so it is exempted (the only two users of this are Projectile.CanHit and beam weapons).
        public static bool ThingCoveredPrefix(Thing thing)
        {
            return !(thing is Building_WindowGlass);
        }

        // Patch 8: the shot that lands on the half wall under a window hits the window instead. Nothing in the game's
        // target picking can be trusted with this: when a projectile drops into that cell, "the things in this cell" are
        // weighed by fillPercent, and the window's is 0 while the wall's is 1 - so the wall took every hit and the window
        // none (found in play, 2026-10-21). The boards are what a bullet reaches first, so they take the damage.
        public static bool BulletImpactPrefix(Bullet __instance, Thing hitThing)
        {
            if (hitThing == null || hitThing.def == null || !HalfWallState.IsGlazed(hitThing.def))
            {
                return true;
            }
            Map map = hitThing.Map;
            if (map == null)
            {
                return true;
            }
            Building_WindowGlass window = WindowAt(hitThing.Position, map);
            if (window == null || window == hitThing)
            {
                return true;
            }
            // Only a window that is itself in the way takes the hit: a wide open one leaves the opening clear, so a
            // bullet that lands on the wall under it was aiming at the wall and damages the wall (user, 2026-10-21).
            if (HitBlockChance(hitThing.Position, map) <= 0f)
            {
                return true;
            }
            DamageInfo dinfo = new DamageInfo(__instance.def.projectile.damageDef, __instance.DamageAmount,
                __instance.ArmorPenetration, 0f, __instance.Launcher, null, __instance.EquipmentDef,
                DamageInfo.SourceCategory.ThingOrUnknown, null);
            window.TakeDamage(dinfo);
            __instance.Destroy();

            return false;
        }

        // Patch 7: which piece of cover a stopped shot lands on is picked by weight, and the half wall under a window is
        // always the heavier of the two - so the shot would damage the wall and leave the window untouched (found in
        // play, 2026-10-21). When the game picks that wall, the window in the same cell takes the hit instead.
        public static void GetCoverPostfix(ref Thing __result)
        {
            if (__result == null || __result is Building_WindowGlass || __result.def == null || !AnyOpen)
            {
                return;
            }
            if (!HalfWallState.IsGlazed(__result.def))
            {
                return;
            }
            Map map = __result.Map;
            if (map == null)
            {
                return;
            }
            Building_WindowGlass window = WindowAt(__result.Position, map);
            if (window != null && OpenAt(__result.Position, map))
            {
                __result = window;
            }
        }

        // Patch 6: the last word on whether a projectile may land on a window. ThingCovered is a two-line loop that the
        // JIT is free to inline into CanHit (and an inlined copy cannot see a patch), so the exemption is repeated on the
        // bigger method as well: without it the shot aimed at the window falls through to "the things in this cell" and
        // damages the half wall under it instead (found in play, 2026-10-21).


        public static void CanHitPostfix(Thing thing, ref bool __result)
        {
            if (!(thing is Building_WindowGlass)) return;
            if (__result || !(thing is Building_WindowGlass window) || !window.Spawned)
            {
                return;
            }
            __result = true;
        }
    }
}