using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Glazing fitted on top of a half wall: a SECOND thing in the half wall's own cell.
    //   isEdifice = false  -> the half wall keeps the cell's edifice slot (verified: they coexist)
    //   not isAttachment   -> WallMount leaves it alone (an attachment is shifted a cell off the wall and turned 180,
    //                         which is what made the earlier attempts look wrong)
    //
    // The window is a real building: ordered as a blueprint (the "Fit window" button on the wall, or the Structure
    // menu), built by a colonist for two fifths of a door's cost, and taken out with the normal Deconstruct.
    public class Building_WindowGlass : Building
    {
        public const string ClosedDefName = "PZ_WindowGlass";
        public const string OpenDefName = "PZ_WindowGlassOpen";

        public CompWindowLatch Latch
        {
            get { return GetComp<CompWindowLatch>(); }
        }

        // The glazing has no faction of its own, so without this the game treats it as an unclaimed building
        // (right-click "Claim" before it counts as the player's). It belongs to whoever owns the half wall it sits in.
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            AdoptWallFaction();
            // A window turns the barricade it is fitted on into a wall: impassable, holding a roof, closing a room.
            HalfWallState.Glaze(HalfWallState.WallAt(Position, map), Latch != null && Latch.openPct > 0.001f);
        }

        // Taking the window out returns the wall to a barricade (a bare half wall).
        public override void Destroy(DestroyMode mode)
        {
            Building wall = Spawned ? HalfWallState.WallAt(Position, Map) : null;
            base.Destroy(mode);
            if (!HalfWallState.WallGoingDown)
            {
                HalfWallState.Strip(wall);
            }
        }

        public void AdoptWallFaction()
        {
            Map map = Map;
            if (map == null || Faction != null)
            {
                return;
            }
            Building wall = Position.GetEdifice(map);
            if (wall != null && wall.Faction != null)
            {
                SetFaction(wall.Faction);
            }
        }

        // Vacuum (Odyssey): the def is isAirtight, so shut the window seals a room exactly like a brick wall. From the
        // first crack the boards are open, so the room leaks like it does through a fully open door. IsAirtight is what
        // the game asks a building on a room's boundary.
        public override bool IsAirtight
        {
            get
            {
                CompWindowLatch latch = Latch;
                return latch == null || latch.openPct <= 0.001f;
            }
        }

        // How long a colonist takes to slide the boards: the same stat a door of this stuff opens at, so a wooden
        // window opens exactly as fast as a wooden door.
        public float TicksToToggle
        {
            get
            {
                float speed = this.GetStatValue(StatDefOf.DoorOpenSpeed);
                return speed > 0.01f ? 60f / speed : 60f;
            }
        }

        // No gizmo of our own: the window is opened by the comp's toggle (a colonist does the work) and taken out with
        // the normal Deconstruct designation, like any other building.

        // The frame is baked by IsoRim's model definition; the two sashes are drawn from their own model every frame so
        // they can slide apart over the opening as a colonist works the window. That is why the window is
        // drawerType RealtimeOnly.
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            CompWindowLatch latch = Latch;
            WindowSashes.Draw(this, latch != null ? latch.openPct : 0f);
        }
    }
}
