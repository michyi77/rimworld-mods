using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // A window that takes the wall's own cell (SPEC-window.md, revised 2026-10-19).
    //
    // It is a wall in every way the game cares about: the cell's edifice, holds the roof, blocks
    // wind, impassable, so the room boundary and pathing are untouched. Only the 3D model differs -
    // solid for the lower half, a real through-hole with a window in the upper half.
    //
    // Open and closed are three ThingDefs sharing this class, one IsoRim model each, so the toggle
    // is visible without depending on any of IsoRim's internal state machinery. The state lives in
    // the def, so it needs no Scribe and survives save/load for free. Swapping keeps stuff, faction,
    // rotation and the hit point fraction.
    //
    // "Outward" has no meaning for a wall, so the player picks the side: OpenA and OpenB swing the
    // sashes toward the two faces.
    public class Building_WindowWall : Building
    {
        public const string ClosedDefName = "PZ_WindowWall";
        public const string OpenADefName = "PZ_WindowWallOpenA";
        public const string OpenBDefName = "PZ_WindowWallOpenB";

        private bool IsClosed
        {
            get { return def != null && def.defName == ClosedDefName; }
        }

        public bool Open
        {
            get { return !IsClosed; }
        }

        // How long the sashes take to swing, in ticks: the stat a door of this stuff opens at, so a
        // window opens exactly as fast as a door of the same stuff. Used from M2 on.
        public float TicksToToggle
        {
            get
            {
                float speed = this.GetStatValue(StatDefOf.DoorOpenSpeed);
                return 60f / Mathf.Max(0.01f, speed);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            if (IsClosed)
            {
                yield return OpenGizmo(OpenADefName, "Open window (this side)",
                    "Swing the sashes out to this face of the wall. Light and air pass through.");
                yield return OpenGizmo(OpenBDefName, "Open window (other side)",
                    "Swing the sashes out to the opposite face of the wall. Light and air pass through.");
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "Close window",
                    defaultDesc = "Shut the sashes: no light or air passes through.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Vent"),
                    action = () => SwapTo(ClosedDefName)
                };
            }
        }

        private Gizmo OpenGizmo(string defName, string label, string desc)
        {
            return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Vent"),
                action = () => SwapTo(defName)
            };
        }

        private void SwapTo(string defName)
        {
            Map map = Map;
            if (map == null)
            {
                return;
            }

            ThingDef want = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (want == null)
            {
                Log.Error("[ProjectZ] window wall: def " + defName + " is missing, cannot toggle");
                return;
            }

            IntVec3 pos = Position;
            Rot4 rot = Rotation;
            ThingDef stuff = Stuff;
            Faction fac = Faction;
            int oldHp = HitPoints;
            int oldMax = MaxHitPoints;

            // Build the replacement first, so a failure cannot leave a hole in the wall run.
            Building_WindowWall fresh = ThingMaker.MakeThing(want, stuff) as Building_WindowWall;
            if (fresh == null)
            {
                Log.Error("[ProjectZ] window wall: " + defName + " is not a Building_WindowWall");
                return;
            }

            Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(fresh, pos, map, rot);
            if (fac != null)
            {
                fresh.SetFactionDirect(fac);
            }

            int newMax = fresh.MaxHitPoints;
            float frac = oldMax > 0 ? (float)oldHp / oldMax : 1f;
            fresh.HitPoints = Mathf.Clamp(Mathf.RoundToInt(newMax * frac), 1, newMax);
        }
    }
}
