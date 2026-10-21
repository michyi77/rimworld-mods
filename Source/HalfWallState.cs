using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // A bare half wall is a barricade, not a wall: pawns climb over it, so a ring of them encloses no room, and it holds
    // up no roof. Only once a window is fitted does the cell become a wall - impassable, holding a roof - so a room can
    // be closed with half walls that all have windows.
    //
    // The state is carried by which def the wall has: PZ_HalfWall (barricade) or PZ_HalfWallGlazed (wall). Swapping the
    // def lets the room, roof, light and pathing code see the change with no patch of its own; the game rebuilds the
    // regions and the roof support by itself when a building changes.
    public static class HalfWallState
    {
        // Raised while a glazed wall is being destroyed: its window must not bring a bare wall back (see the
        // GlazedWallTakesItsWindow patch).
        public static bool WallGoingDown;

        // Raised while this class swaps one half wall kind for another: that swap destroys the old wall, and the
        // "wall goes down, the window goes with it" patch must not take the window away for it.
        public static bool Swapping;

        public const string BareDefName = "PZ_HalfWall";
        public const string GlazedDefName = "PZ_HalfWallGlazed";

        // The same wall with the window slid open: it exchanges vacuum like a vent, so the game lets the room's vacuum
        // rise by itself, smoothly, instead of us writing Room.Vacuum behind its back (which it recomputed away).
        public const string GlazedOpenDefName = "PZ_HalfWallGlazedOpen";

        // Either kind of half wall: the barricade one, or the same wall with a window in it.
        public static bool IsHalfWall(ThingDef def)
        {
            return def != null && (def.defName == BareDefName || IsGlazed(def));
        }

        // Either of the two "wall with a window" kinds.
        public static bool IsGlazed(ThingDef def)
        {
            return def != null && (def.defName == GlazedDefName || def.defName == GlazedOpenDefName);
        }

        // The half wall in this cell, of either kind, or null.
        public static Building WallAt(IntVec3 c, Map map)
        {
            if (map == null)
            {
                return null;
            }
            Building b = c.GetEdifice(map);
            if (b == null || b.def == null)
            {
                return null;
            }
            return IsHalfWall(b.def) ? b : null;
        }

        // A window has just been fitted (or loaded): its half wall becomes the wall kind, sealed or exchanging vacuum
        // depending on whether the boards are open.
        public static void Glaze(Building wall, bool open)
        {
            Swap(wall, open ? GlazedOpenDefName : GlazedDefName);
        }

        // The boards were slid open or shut: the wall changes kind with them.
        public static void SetOpen(Building wall, bool open)
        {
            Swap(wall, open ? GlazedOpenDefName : GlazedDefName);
        }

        // The window is gone: back to a barricade.
        public static void Strip(Building wall)
        {
            Swap(wall, BareDefName);
        }

        private static void Swap(Building wall, string defName)
        {
            if (wall == null || wall.Destroyed || !wall.Spawned || wall.def == null || wall.def.defName == defName)
            {
                return;
            }
            ThingDef want = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (want == null)
            {
                Log.Error("[ProjectZ] windows: half wall def " + defName + " is missing");
                return;
            }
            Map map = wall.Map;
            IntVec3 pos = wall.Position;
            KeepRoom(map, pos);
            Rot4 rot = wall.Rotation;
            Faction fac = wall.Faction;
            ThingDef stuff = wall.Stuff;
            int hp = wall.HitPoints;
            int max = wall.MaxHitPoints;
            Building fresh = ThingMaker.MakeThing(want, stuff) as Building;
            if (fresh == null)
            {
                return;
            }
            Swapping = true;
            try
            {
                wall.Destroy(DestroyMode.Vanish);
                GenSpawn.Spawn(fresh, pos, map, rot);
            }
            finally
            {
                Swapping = false;
            }
            if (fac != null)
            {
                fresh.SetFaction(fac);
            }
            int newMax = fresh.MaxHitPoints;
            fresh.HitPoints = Mathf.Clamp(Mathf.RoundToInt(newMax * (max > 0 ? (float)hp / max : 1f)), 1, newMax);
        }

        // The swap destroys the wall and spawns another in its place, and the game may then rebuild the room around it - a
        // freshly made Room starts at the outdoor temperature (RoomTempTracker's constructor does exactly that), so a
        // room's own heat and vacuum jumped to the outside figures just for taking a window out, then drifted back
        // (user, 2026-10-21). What the room held is read before the swap and put back once the rooms are rebuilt.
        private struct KeptRoom
        {
            public Map map;
            public IntVec3 cell;
            public float temperature;
            public float vacuum;
            public int until;
        }

        private static readonly System.Collections.Generic.List<KeptRoom> keptRooms =
            new System.Collections.Generic.List<KeptRoom>();

        public static void TryPatch(HarmonyLib.Harmony harmony)
        {
            System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(typeof(RegionAndRoomUpdater),
                "TryRebuildDirtyRegionsAndRooms");
            if (target == null)
            {
                Log.ErrorOnce("[ProjectZ] windows: the room updater hook is missing; a room will lose its heat when a "
                    + "window is fitted or taken out", 0x5A17F0);
                return;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(HalfWallState), nameof(RestoreRooms)));
            }
            catch (System.Exception e)
            {
                Log.ErrorOnce("[ProjectZ] windows: could not hook the room updater: " + e, 0x5A17F1);
            }
        }

        private static void KeepRoom(Map map, IntVec3 cell)
        {
            if (map == null)
            {
                return;
            }
            Room room = cell.GetRoom(map);
            if (room == null || room.UsesOutdoorTemperature)
            {
                return;
            }
            KeptRoom kept = new KeptRoom();
            kept.map = map;
            kept.cell = cell;
            kept.temperature = room.Temperature;
            kept.vacuum = room.Vacuum;
            kept.until = Find.TickManager.TicksGame + 5;
            keptRooms.Add(kept);
        }

        // Called right after the game rebuilds its rooms for the tick.
        public static void RestoreRooms()
        {
            if (keptRooms.Count == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            for (int i = keptRooms.Count - 1; i >= 0; i--)
            {
                KeptRoom kept = keptRooms[i];
                if (kept.until < now || kept.map == null)
                {
                    keptRooms.RemoveAt(i);
                    continue;
                }
                Room room = kept.cell.GetRoom(kept.map);
                if (room == null)
                {
                    continue;   // no room there yet: another rebuild may still come
                }
                keptRooms.RemoveAt(i);
                if (room.UsesOutdoorTemperature)
                {
                    continue;   // the room really is open to the sky now: the game's answer stands
                }
                room.Temperature = kept.temperature;
                room.Vacuum = kept.vacuum;
            }
        }
    }
}
