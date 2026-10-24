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
    // Since IsoRim API 20 that drawing is public - IsoRim.Api.DrawGhost(def, cell, rot, colour, stuff, style) - and it
    // is the one IsoRim itself uses under the pointer while placing. It is looked up once so an older IsoRim simply
    // falls back to our own flat ghost instead of throwing MissingMethodException every frame: the internal
    // IsoRim.Ghosts class is not touched any more (its DrawPreview changed signature in the 2026-10 update).
    //
    // Two things make this necessary at all: IsoRim keeps a blueprint's flat sprite on the floor (Baking.cs:119), where
    // the half wall hides it, and the sprite is the *built* thing's graphic, so WindowsMod swaps the window's own sprite
    // for a sub-pixel one and this draws the ghost in its place. Drawn once a frame from the GUI pass, the way the base
    // mod's range shell is: Graphics.DrawMesh from the map drawing pass lands in a pass of its own and does not show.
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.Update))]
    public static class WindowGhosts
    {
        // IsoRim's own blueprint and frame colours (Ghosts.BlueprintColor / Ghosts.FrameColor). The alpha is forced to
        // 0.45 by IsoRim's ghost material; ours is only used by the flat fallback.
        private static readonly Color Blueprint = new Color(0.42f, 0.70f, 1f, 0.55f);
        private static readonly Color Sawdust = new Color(0.96f, 0.86f, 0.64f, 0.55f);

        private static int drawnFrame = -1;
        private static bool logged;

        // The public API is probed once: "IsoRim.Api.DrawGhost" only exists from API 20 on, and a direct call compiled
        // against it would fail to JIT on an older IsoRim.
        private static bool apiChecked;
        private static bool apiPresent;

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
                Color colour = frame ? Sawdust : Blueprint;
                if (TryIsoRim(window, t, colour))
                {
                    Note(frame, "through the IsoRim ghost API");
                    continue;
                }
                Flat(window, t, colour);
                Note(frame, "flat (IsoRim's ghost API is not available)");
            }
        }

        private static void Note(bool frame, string how)
        {
            if (logged)
            {
                return;
            }
            logged = true;
            Log.Message("[ProjectZ] windows: drawing " + (frame ? "frame" : "blueprint") + " ghosts " + how);
        }

        // ---------------------------------------------------------------- IsoRim's public API

        private static bool IsoRimApi
        {
            get
            {
                if (!apiChecked)
                {
                    apiChecked = true;
                    try
                    {
                        Type api = AccessTools.TypeByName("IsoRim.Api");
                        apiPresent = api != null
                            && AccessTools.Method(api, "DrawGhost") != null
                            && AccessTools.Property(api, "Version") != null;
                        if (!apiPresent)
                        {
                            Log.Message("[ProjectZ] windows: this IsoRim has no public ghost API (needs API 20+), "
                                + "drawing flat window ghosts instead");
                        }
                    }
                    catch (Exception e)
                    {
                        apiPresent = false;
                        Log.Warning("[ProjectZ] windows: could not read IsoRim's ghost API: " + e.Message);
                    }
                }
                return apiPresent;
            }
        }

        private static bool TryIsoRim(ThingDef def, Thing t, Color colour)
        {
            if (!IsoRimApi)
            {
                return false;
            }
            try
            {
                return DrawThroughApi(def, t, colour);
            }
            catch (Exception e)
            {
                // An IsoRim that answers the probe but throws anyway (a half-updated install): stop asking, and let
                // the flat ghost take over for the rest of the session.
                apiPresent = false;
                Log.ErrorOnce("[ProjectZ] windows: IsoRim's ghost API failed, drawing flat ghosts instead: " + e, 0x5A17C6);
                return false;
            }
        }

        // Kept in its own method on purpose: this is the only place that touches IsoRim's API directly, so it is
        // JIT-ed only after the probe above has found the method.
        private static bool DrawThroughApi(ThingDef def, Thing t, Color colour)
        {
            if (IsoRim.Api.Version < 20)
            {
                return false;
            }
            return IsoRim.Api.DrawGhost(def, t.Position, t.Rotation, colour, t.Stuff, t.StyleDef);
        }

        // ---------------------------------------------------------------- our own flat ghost

        // A see-through flat sprite at the window's height, for an IsoRim too old for the public API (or one whose 3D
        // view is switched off): the same look the game gives every other blueprint, just lifted off the floor.
        private static void Flat(ThingDef def, Thing t, Color colour)
        {
            try
            {
                Graphic graphic = colour == Sawdust ? WindowGhostArts.Sawdust : WindowGhostArts.Blueprint;
                Vector3 loc = t.Position.ToVector3ShiftedWithAltitude(AltitudeLayer.BuildingOnTop);
                graphic.DrawWorker(loc, t.Rotation, def, null, 0f);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[ProjectZ] drawing a flat window ghost failed: " + e, 0x5A17C7);
            }
        }
    }

    // The flat fallback's two sprites are made once, in a static constructor: RimWorld asks for exactly that of any
    // type holding a Graphic, and it keeps the asset load on the main thread at startup.
    [StaticConstructorOnStartup]
    internal static class WindowGhostArts
    {
        internal static readonly Graphic Blueprint = FlatGraphic(new Color(0.42f, 0.70f, 1f, 0.55f));
        internal static readonly Graphic Sawdust = FlatGraphic(new Color(0.96f, 0.86f, 0.64f, 0.55f));

        private static Graphic FlatGraphic(Color colour)
        {
            // The window's own flat sprite, not the menu icon: it is what the game would draw for the window in 2D.
            return GraphicDatabase.Get<Graphic_Single>("ProjectZ/Window", ShaderDatabase.Transparent,
                new Vector2(1f, 1f), colour);
        }
    }
}
