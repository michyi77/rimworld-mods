using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ProjectZ.Windows
{
    [DefOf]
    public static class PZWindowDefOf
    {
        public static JobDef PZ_OperateWindow;

        static PZWindowDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(PZWindowDefOf));
        }
    }

    public class CompProperties_WindowLatch : CompProperties
    {
        public CompProperties_WindowLatch()
        {
            compClass = typeof(CompWindowLatch);
        }
    }

    // How far the boards have slid: 0 shut, 1 fully apart. The player only says what they want (wantOpen, saved); a
    // colonist walks over and does it - WorkGiver_WindowLatch hands out JobDriver_OperateWindow, which reaches the
    // window and has a working colonist there for a door's opening time, while `animating` slides openPct across and
    // Building_WindowGlass.DrawAt moves the two sash models with it.
    //
    // The def never changes: PZ_WindowGlassOpen stays only for saves that already hold one, and its openPct is 1.
    //
    // Unlike a flickable heater this has no power comp and no power switch, so the game draws no "switched off" icon
    // over the window.
    public class CompWindowLatch : ThingComp
    {
        // What the player asked for (the gizmo).
        public bool wantOpen;

        // Where the boards are: 0 shut, 1 apart. Moved only while a colonist is working the window.
        public float openPct;

        // One line per sideways window, so a report of "it does not ventilate" can be checked against what the addon
        // thinks of the window.
        private static readonly System.Collections.Generic.HashSet<int> reportedSideways =
            new System.Collections.Generic.HashSet<int>();

        // A colonist is sliding the boards right now.
        public bool animating;

        // How far the vacuum has been let in through this window: only ever goes up while the boards are open, so a
        // room's vacuum pump cannot drag the figure back and forth (see LetVacuumIn).
        public float vacuumLetIn;

        // A vent's exchange rate per Rare tick (the game's own number for Building_Vent) and its cadence.
        public const float VentRate = 4f;

        public const int VentTicks = 250;

        // Share of the way to full vacuum an open window lets in per tick: 1/240 is four seconds (240 ticks) from a
        // breathable room to full vacuum at normal speed, and slower while the boards are only part way open.
        public const float VacuumPerTick = 1f / 240f;

        // LIGHT (user, 2026-10-20): what a window lets through - daylight, or a lamp's light from the other room - is
        // worked out cell by cell in WindowLight, because the glow grid's blocker is a yes/no and its lights shine every
        // way. Here it is only asked for again: on every step of the boards, and now and then so the light follows the
        // sun and whatever lamps are about.
        private const int LightCheckTicks = 60;


        public Building_WindowGlass Window
        {
            get { return parent as Building_WindowGlass; }
        }

        public bool IsOpen
        {
            get { return openPct >= 0.999f; }
        }

        // A colonist still has something to do: the boards are not at the end the player asked for. (Comparing wantOpen
        // with IsOpen is wrong here: a half-shut window "is not open" and would stop asking for work.)
        public bool WantsWork
        {
            get { return Window != null && (wantOpen ? openPct < 0.999f : openPct > 0.001f); }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // A save from before this comp: the state is the def it was saved with (the open one is fully apart).
            bool savedOpen = parent != null && parent.def != null && parent.def.defName == Building_WindowGlass.OpenDefName;
            Scribe_Values.Look(ref openPct, "openPct", savedOpen ? 1f : 0f);
            Scribe_Values.Look(ref wantOpen, "wantOpen", openPct >= 0.999f);
            Scribe_Values.Look(ref vacuumLetIn, "vacuumLetIn", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // The half wall may have been read after this thing, so its faction was not there in SpawnSetup.
                Building_WindowGlass w = Window;
                if (w != null)
                {
                    w.AdoptWallFaction();
                }
                // The sight table is not saved: a window that comes back fully open has to be registered again.
                WindowSight.Sync(w);
            }
        }

        // The doors' pace, driven while a colonist is at the window: the job says how long the work lasts and this
        // slides the boards across in step with it.
        public override void CompTick()
        {
            base.CompTick();

            // A window set across the wall is always open to the air, whatever its boards are doing: they slide along the
            // wall and cover nothing (user, 2026-10-21). It is never open to a shot either - that is in WindowSight.
            bool sideways;
            IntVec3 axis = WindowOrientation.Axis(Window, out sideways);
            float airOpen = sideways ? 1f : openPct;
            if (sideways && Window != null && reportedSideways.Add(Window.thingIDNumber))
            {
                Log.Message("[ProjectZ] windows: window at " + Window.Position
                    + " is set across the wall (sideways): it ventilates and leaks vacuum always, and shots pass as a slot");
            }

            // Ventilation: heat passes through the way a vent passes it when the boards are open, and not at all when
            // they are shut - x + n*(y - x) with x = a wall's exchange (none) and y = a vent's. GenTemperature's own
            // equalisation is the vent's mechanism, so the two numbers stay the game's: rate = VentRate * openPct, on
            // the vent's own cadence (Rare = every 250 ticks).
            //
            // twoWay only looks along the building's own facing axis, and a sideways window faces along the wall - both
            // of those cells are wall, so it finds no room at all and returns (user, 2026-10-21: a sideways window did
            // not ventilate). For that one the four-way scan is used instead; it finds the same two rooms.
            if (Window != null && airOpen > 0.001f && parent.IsHashIntervalTick(VentTicks))
            {
                Vent(Window, axis, sideways, VentRate * airOpen);
            }

            if (Window != null && airOpen > 0.001f)
            {
                // Every tick, so the climb is smooth and the room's vacuum pump cannot pull the figure down between
                // steps: 0 to full vacuum in about four seconds, slower while the boards are only part way open.
                StepVacuum(airOpen);
                HoldVacuum();
            }

            // The daylight is asked for again now and then so it follows the sun; between those checks it only moves when
            // the boards cross the "open" line (SetOpenPct). The sight table is refreshed here too, because which way a
            // window counts as open depends on the walls around it, and those can be built or taken down.
            if (parent.IsHashIntervalTick(LightCheckTicks))
            {
                WindowLight.RequestRefresh(parent.Map);
                WindowSight.Sync(Window);
            }

            if (!animating || Window == null)
            {
                return;
            }
            float step = 1f / Mathf.Max(1f, Window.TicksToToggle);
            SetOpenPct(Mathf.MoveTowards(openPct, wantOpen ? 1f : 0f, step));
        }

        // Every change of the boards goes through here: crossing "shut" (and only that) changes whether the cell is
        // airtight, and the game's vacuum build has to be told to have another look (Odyssey).
        public void SetOpenPct(float value)
        {
            value = Mathf.Clamp01(value);
            bool wasSealed = openPct <= 0.001f;
            openPct = value;
            if (wasSealed != (openPct <= 0.001f))
            {
                // The wall under the window changes kind with the boards: sealed while shut, vacuum-exchanging while
                // open. The game's own vacuum equalisation then raises the room's vacuum smoothly, and it cannot fight
                // us over a value we wrote ourselves (which it recomputed away, and the figure jumped 0-2%).
                bool open = openPct > 0.001f;
                if (parent != null && parent.Spawned)
                {
                    HalfWallState.SetOpen(HalfWallState.WallAt(parent.Position, parent.Map), open);
                }
                // Opening starts the leak from wherever the room is now; shutting ends it. A sideways window never shuts
                // against the air, so its leak is not interrupted (user, 2026-10-21).
                if (!WindowOrientation.IsSideways(Window))
                {
                    vacuumLetIn = 0f;
                }
                NotifyVacuumChanged();
            }
            // Every step of the boards moves the light: shut the cell is a wall's, cracked open a sliver's, fully open
            // the day's. (It throttles itself, so this is safe to call on every tick of the slide.)
            SyncLight();
            // And the same step decides when the window can be shot through: only once the boards are fully apart.
            WindowSight.Sync(Window);
        }

        // Heat through the opening, in the three cases there are: two enclosed rooms, a room and the outdoors, and
        // outdoors on both sides. The game's own equalisation is used for the first (it knows about vacuum maps), and it
        // is what the vent's numbers come from; it only ever moves rooms that are not outdoor ones, so the outdoor case is
        // done here with the same arithmetic: the room drifts toward the mean of itself and the outdoor temperature, at
        // the vent's rate per its own size (user, 2026-10-21: a window should ventilate with the outside as well).
        private void Vent(Building_WindowGlass window, IntVec3 axis, bool sideways, float rate)
        {
            Map map = window.Map;
            if (map == null)
            {
                return;
            }
            Room a = RoomAt(map, window.Position + axis);
            Room b = RoomAt(map, window.Position - axis);
            bool aOut = Outside(a);
            bool bOut = Outside(b);
            if (aOut && bOut)
            {
                return;
            }
            if (aOut || bOut)
            {
                VentToOutdoors(map, aOut ? b : a, rate);
                return;
            }
            GenTemperature.EqualizeTemperaturesThroughBuilding(window, rate, !sideways);
        }

        private static Room RoomAt(Map map, IntVec3 c)
        {
            return c.InBounds(map) ? c.GetRoom(map) : null;
        }

        private static bool Outside(Room room)
        {
            return room == null || room.UsesOutdoorTemperature;
        }

        private static void VentToOutdoors(Map map, Room inside, float rate)
        {
            if (inside == null || inside.UsesOutdoorTemperature)
            {
                return;
            }
            float outdoors = map.mapTemperature.OutdoorTemp;
            float average = (inside.Temperature + outdoors) * 0.5f;
            float cells = Mathf.Max(1f, inside.CellCount);
            inside.Temperature += (average - inside.Temperature) * rate / cells;
        }

        // Vacuum (Odyssey): while the boards are open the outside vacuum seeps in. It only ever rises - a step of
        // VacuumPerTick every tick, in proportion to how far the boards are open, never more than the outside - so the
        // figure climbs smoothly to 100% in about four seconds instead of the 100%/48% bounce the game's own vent
        // exchange produces when the room also has a vacuum pump. Shut the window and this stops; the pump pulls the
        // room back to 0%.
        private void StepVacuum(float howOpen)
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }
            float outside = OutsideVacuum(map);
            if (outside <= 0.01f)
            {
                return;   // a breathable map: rooms never gain vacuum
            }
            vacuumLetIn = Mathf.Min(outside, Mathf.Max(vacuumLetIn, 0f) + VacuumPerTick * howOpen);
        }

        // Puts the rooms back on the ramp every tick: whatever the game's own vacuum work did to them, they are never
        // below what has already been let in while the window is open.
        private void HoldVacuum()
        {
            Map map = parent.Map;
            if (map == null || vacuumLetIn <= 0.001f)
            {
                return;
            }
            // The real opening axis: a window set across the wall opens along it, not along its own facing.
            bool sideways;
            IntVec3 axis = WindowOrientation.Axis(Window, out sideways);
            Raise(map, parent.Position + axis);
            Raise(map, parent.Position - axis);
        }

        private void Raise(Map map, IntVec3 c)
        {
            if (!c.InBounds(map))
            {
                return;
            }
            Room room = c.GetRoom(map);
            if (room != null && room.Vacuum < vacuumLetIn)
            {
                room.Vacuum = vacuumLetIn;
            }
        }

        // The vacuum of a cell no room encloses: the map corner, which is space on an orbital map and open air on a
        // breathable one.
        private static float OutsideVacuum(Map map)
        {
            IntVec3 corner = new IntVec3(0, 0, 0);
            if (!corner.InBounds(map))
            {
                corner = new IntVec3(1, 0, 1);
            }
            return VacuumUtility.GetVacuum(corner, map);
        }

        private void NotifyVacuumChanged()
        {
            if (parent == null || !parent.Spawned || parent.Map == null)
            {
                return;
            }
            VacuumComponent vacuum = parent.Map.GetComponent<VacuumComponent>();
            if (vacuum != null)
            {
                vacuum.Dirty();
            }
        }

        // Asks for the light to be worked out again: the boards moved, or it is time to look at the sun and the lamps
        // again. WindowLight answers for the whole map and throttles itself, so this is safe on every step of the slide.
        private void SyncLight()
        {
            if (parent.Spawned)
            {
                WindowLight.RequestRebuild(parent.Map);
            }
        }

        // A window that is built (or loaded) fully open is a firing slit from the start; a shut one is a wall.
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            WindowSight.Sync(Window);
        }

        // The window is gone: its light goes with it on the next look (the map load or the slow refresh clears it, and a
        // full rebuild here would scan the whole map once per destroyed window), and its cell goes back to being a wall.
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            WindowSight.Forget(Window, map);
            WindowLight.RequestRebuild(map);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // No faction test: the window takes the half wall's faction when it spawns, and the button has to be there
            // even for one that somehow ended up unclaimed.
            Command_Toggle cmd = new Command_Toggle
            {
                defaultLabel = wantOpen ? "Close window" : "Open window",
                defaultDesc = wantOpen
                    ? "Have a colonist slide the boards shut."
                    : "Have a colonist slide the boards apart: light and air pass through.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Vent"),
                isActive = () => wantOpen,
                toggleAction = () => { wantOpen = !wantOpen; }
            };
            yield return cmd;
        }
    }

    // Basic work, like flicking a switch: everyone can be sent, and the Work tab needs no new column.
    public class WorkGiver_WindowLatch : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode
        {
            get { return PathEndMode.Touch; }
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Map == null)
            {
                yield break;
            }
            // The window defs are not buildings of any faction (the half wall keeps the cell's edifice), so they are
            // not in allBuildingsColonist: ask listerThings for their defs instead.
            for (int d = 0; d < 2; d++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(
                    d == 0 ? Building_WindowGlass.ClosedDefName : Building_WindowGlass.OpenDefName);
                if (def == null)
                {
                    continue;
                }
                List<Thing> list = pawn.Map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < list.Count; i++)
                {
                    yield return list[i];
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_WindowGlass w = t as Building_WindowGlass;
            if (w == null || w.Latch == null || !w.Latch.WantsWork)
            {
                return false;
            }
            if (w.IsForbidden(pawn))
            {
                return false;
            }
            return pawn.CanReserveAndReach(w, PathEndMode.Touch, Danger.Some);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(PZWindowDefOf.PZ_OperateWindow, t);
        }
    }

    public class JobDriver_OperateWindow : JobDriver
    {
        private const TargetIndex WindowInd = TargetIndex.A;

        private Building_WindowGlass Window
        {
            get { return job.targetA.Thing as Building_WindowGlass; }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(WindowInd);

            // The goal is fixed when the job starts. It must NOT be `Latch.WantsWork`: the boards are already moving
            // away from "wants work" the moment the animation starts, so closing (open -> shut) failed on the first
            // tick and the window stayed open.
            Building_WindowGlass target = Window;
            bool goal = target != null && target.Latch != null && target.Latch.wantOpen;
            this.FailOn(() =>
            {
                Building_WindowGlass w = Window;
                return w == null || w.Latch == null || w.Latch.wantOpen != goal;
            });
            yield return Toils_Goto.GotoThing(WindowInd, PathEndMode.Touch);

            // The boards travel the rest of the way (a half-opened window carries on from where it stopped), so the
            // work lasts as long as the pieces still have to move - at a door's speed.
            float distance = 1f;
            int duration = 60;
            if (target != null && target.Latch != null)
            {
                distance = Mathf.Clamp01(Mathf.Abs((goal ? 1f : 0f) - target.Latch.openPct));
                duration = Mathf.Max(20, Mathf.RoundToInt(distance * target.TicksToToggle));
            }

            Toil work = ToilMaker.MakeToil("OperateWindow");
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = duration;
            work.WithProgressBar(WindowInd, () => 1f - (float)pawn.jobs.curDriver.ticksLeftThisToil / Mathf.Max(1f, duration));
            work.initAction = () =>
            {
                Building_WindowGlass w = Window;
                if (w != null && w.Latch != null)
                {
                    w.Latch.animating = true;   // CompTick slides openPct from here
                }
            };
            work.AddFinishAction(() =>
            {
                // Interrupted: the boards simply stay where they got to.
                Building_WindowGlass w = Window;
                if (w != null && w.Latch != null)
                {
                    w.Latch.animating = false;
                }
            });
            yield return work;

            // Finished: settle exactly on the wish (the tick's last step may be a hair short).
            Toil settle = ToilMaker.MakeToil("WindowSashSettled");
            settle.defaultCompleteMode = ToilCompleteMode.Instant;
            settle.initAction = () =>
            {
                Building_WindowGlass w = Window;
                if (w != null && w.Latch != null)
                {
                    w.Latch.animating = false;
                    w.Latch.SetOpenPct(w.Latch.wantOpen ? 1f : 0f);
                }
            };
            yield return settle;
        }
    }
}
