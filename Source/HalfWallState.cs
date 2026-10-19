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
    }
}
