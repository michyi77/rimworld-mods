using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // A window built into a wall (SPEC-window.md).
    //
    // The wall stays: this is a wall attachment (isEdifice=false, isAttachment=true), so the cell's
    // edifice and the room boundary are still the wall's, and nothing here touches passability -
    // pawns cannot cross because the wall cannot.
    //
    // Open and closed are two ThingDefs (PZ_Window / PZ_WindowOpen) with one IsoRim model each, so
    // the toggle is visible in 3D without depending on any of IsoRim's internal state machinery:
    // the state lives in the def, which also means it needs no Scribe and survives save/load for
    // free. Toggling swaps the thing in place, keeping stuff, faction and the hit point fraction.
    //
    // M2 (light) and M3 (venting) read Open; M4 (half-height cover) is ProjectZ's own LOS rule.
    public class Building_Window : Building
    {
        private const string ClosedDefName = "PZ_Window";
        private const string OpenDefName = "PZ_WindowOpen";

        public bool Open
        {
            get { return def != null && def.defName == OpenDefName; }
        }

        // How long the pane takes to swing, in ticks: the same stat a door of this stuff opens at,
        // so a window opens exactly as fast as a door of the same stuff. Used from M2 on.
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

            bool isOpen = Open;
            yield return new Command_Toggle
            {
                // M1 uses plain text; keyed translations come with the rest of the UI work.
                defaultLabel = isOpen ? "Close window" : "Open window",
                defaultDesc = isOpen
                    ? "Close this window: no light or air passes through it."
                    : "Open this window: light and air pass through it.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Vent"),
                isActive = () => Open,
                toggleAction = Toggle
            };
        }

        private void Toggle()
        {
            Map map = Map;
            if (map == null)
            {
                return;
            }

            string wantName = Open ? ClosedDefName : OpenDefName;
            ThingDef want = DefDatabase<ThingDef>.GetNamedSilentFail(wantName);
            if (want == null)
            {
                Log.Error("[ProjectZ] window: def " + wantName + " is missing, cannot toggle");
                return;
            }

            IntVec3 pos = Position;
            Rot4 rot = Rotation;
            ThingDef stuff = Stuff;
            Faction fac = Faction;
            int oldHp = HitPoints;
            int oldMax = MaxHitPoints;

            // Build the replacement first, so a failure cannot leave a hole in the wall.
            Building_Window fresh = ThingMaker.MakeThing(want, stuff) as Building_Window;
            if (fresh == null)
            {
                Log.Error("[ProjectZ] window: " + wantName + " is not a Building_Window");
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
