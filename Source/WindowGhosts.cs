using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // A planned window is shown the way IsoRim shows every other building's blueprint: as a see-through 3D ghost of the
    // thing it will become, at the thing's own height.
    //
    // IsoRim already has that drawing (Ghosts.DrawPreview, the same call it uses under the pointer while placing), so
    // this borrows it instead of drawing anything of its own: the window's own model - frame and both sashes - in
    // blueprint blue, a frame under construction in the sawdust colour.
    //
    // Two things make this necessary at all: IsoRim keeps a blueprint's flat sprite on the floor (Baking.cs:119), where
    // the half wall hides it, and the sprite is the *built* thing's graphic, so WindowsMod swaps the window's own sprite
    // for a sub-pixel one and this draws the ghost in its place. Drawn once a frame from the GUI pass, the way the base
    // mod's range shell is: Graphics.DrawMesh from the map drawing pass lands in a pass of its own and does not show.
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.Update))]
    public static class WindowGhosts
    {
        // IsoRim's own blueprint and frame colours (Ghosts.BlueprintColor / Ghosts.FrameColor).
        private static readonly Color Blueprint = new Color(0.42f, 0.70f, 1f, 1f);
        private static readonly Color Sawdust = new Color(0.96f, 0.86f, 0.64f, 1f);

        private static int drawnFrame = -1;
        private static bool logged;

        public static void Postfix()
        {
            int frame = Time.frameCount;
            if (drawnFrame == frame)
            {
                return;   // the GUI runs several times a frame; once is enough
            }
            drawnFrame = frame;
            if (!WindowSashes.IsoRimPresent)
            {
                return;
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            try
            {
                Drawn(map, Building_WindowGlass.ClosedDefName);
                Drawn(map, Building_WindowGlass.OpenDefName);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[ProjectZ] drawing a window ghost failed: " + e, 0x5A17C5);
            }
        }

        private static void Drawn(Map map, string windowDefName)
        {
            ThingDef window = DefDatabase<ThingDef>.GetNamedSilentFail(windowDefName);
            if (window == null)
            {
                return;
            }
            Planned(map, window, window.blueprintDef, false);
            Planned(map, window, window.frameDef, true);
        }

        private static void Planned(Map map, ThingDef window, ThingDef planDef, bool frame)
        {
            if (planDef == null)
            {
                return;
            }
            List<Thing> plans = map.listerThings.ThingsOfDef(planDef);
            for (int i = 0; i < plans.Count; i++)
            {
                Thing t = plans[i];
                if (!t.Spawned || t.Position.Fogged(map))
                {
                    continue;
                }
                // The ghost of what it will become, drawn by IsoRim: same model, same height, its own see-through
                // material, its own colour mixing.
                IsoRim.Ghosts.DrawPreview(window, t.Position, t.Rotation, frame ? Sawdust : Blueprint, t.Stuff);
                if (!logged)
                {
                    logged = true;
                    Log.Message("[ProjectZ] windows: drawing " + (frame ? "frame" : "blueprint") + " ghosts through IsoRim");
                }
            }
        }
    }
}
