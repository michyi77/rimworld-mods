using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // The window addon: half walls, sliding glazing, its ventilation, its vacuum seal and the colonist job that works the
    // boards.
    //
    // A separate mod on purpose (2026-10-19): it needs IsoRim and IsoRim ProjectZ, and taking it out leaves the base mod
    // (floors, stairs) exactly as it was - the base assembly has no reference to anything here. All of this assembly's
    // types live in ProjectZ.Windows, so the two mods never collide.
    public class WindowsMod : Mod
    {
        // This mod's folder. The sash model is drawn every frame from Models/ rather than baked from a def, so it is
        // loaded by path at runtime (WindowSashes).
        public static string RootDir;

        public WindowsMod(ModContentPack content) : base(content)
        {
            RootDir = content.RootDir;
            // Never the argument-less PatchAll(): on 1.7 CoreCLR it can patch nothing. A patch that cannot find its
            // target must not take the whole addon down with it, so it is caught and logged.
            try
            {
                Harmony harmony = new Harmony("michyi77.AddWindowsToYourHouse");
                harmony.PatchAll(typeof(WindowsMod).Assembly);
                // Only when IsoRim ProjectZ is installed: its slab height rule must not count a window as too tall.
                WindowIsNotTooTall.TryPatch(harmony);
                // The window light rides on a private GlowGrid method, so it is patched (and reported) on its own.
                WindowLight.TryPatch(harmony);
                // Sight through an open window: three separate hooks, each reported on its own.
                WindowSight.TryPatch(harmony);
                // A half wall swapped for another (fitting, sliding, taking out a window) must not throw the room's heat
                // away: the room is rebuilt right after the swap and a new one starts at the outdoor temperature.
                HalfWallState.TryPatch(harmony);
                // Only while HSK's FPS Control Bridge is installed: its own bullets have to meet a window too.
                FpsBridgeCompat.TryPatch(harmony);
            }
            catch (Exception e)
            {
                Log.Error("[ProjectZ] windows: some patches did not apply, the addon is only partly active: " + e);
            }
            Log.Message("[ProjectZ] windows loaded; needs IsoRim Waylith and IsoRim ProjectZ");
        }

        // A window that is only planned is drawn as a ghost of itself (WindowGhosts), so its blueprint and frame are
        // drawn every frame and their own sprite is made invisible: otherwise the game's flat blueprint square lies on
        // the floor at the foot of the half wall, where it cannot be seen and does not match the window's height
        // (user, 2026-10-19).
        //
        // Called from StaticConstructorOnStartup (WindowPlanSetup), because defs are not loaded yet while a mod's
        // constructor runs.
        public static void PlannedWindowGraphics()
        {
            for (int i = 0; i < 2; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(
                    i == 0 ? Building_WindowGlass.ClosedDefName : Building_WindowGlass.OpenDefName);
                if (def == null)
                {
                    continue;
                }
                // The game draws a blueprint and a frame with the graphic of the thing they will become, so it is that
                // graphic which has to go: an empty texture in its place, one cell across, leaves nothing of the flat
                // sprite on the floor.
                // The window itself, and the blueprint and frame the game generates for it: each carries its own copy
                // of the graphic, made while the defs load, so all three have to lose their sprite.
                Hide(def);
                Hide(def.blueprintDef);
                Hide(def.frameDef);
            }
        }

        private static void Hide(ThingDef d)
        {
            if (d == null)
            {
                return;
            }
            try
            {
                // A frame's def carries no graphic of its own (it draws the thing's), so there is nothing to hide there
                // and no size to read back: this must not turn into a NullReferenceException in the log every load.
                if (d.graphicData != null)
                {
                    // A sub-pixel sprite: nothing of the flat blueprint/frame/preview square is left on the floor, and
                    // the window itself is drawn from its model.
                    d.graphicData.drawSize = new Vector2(0.01f, 0.01f);
                    d.graphicData.texPath = "ProjectZ/Empty";
                    d.graphicData.shaderType = null;
                    // The graphic is cached inside the GraphicData (GraphicData.cachedGraphic): a def that already made
                    // one keeps drawing the old, full-size sprite unless that cache is dropped.
                    System.Reflection.FieldInfo cached = AccessTools.Field(typeof(GraphicData), "cachedGraphic");
                    if (cached != null)
                    {
                        cached.SetValue(d.graphicData, null);
                    }
                    else
                    {
                        Log.Warning("[ProjectZ] windows: GraphicData.cachedGraphic not found; the window's sprite may stay visible");
                    }
                }
                Log.Message("[ProjectZ] windows: " + d.defName + " graphic=" + (d.graphic == null ? "null" : d.graphic.GetType().Name)
                    + (d.graphicData == null
                        ? " no graphicData of its own"
                        : " drawSize=" + d.graphicData.drawSize + " texPath=" + d.graphicData.texPath));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[ProjectZ] hiding a planned window's sprite failed: " + e, 0x5A17C4);
            }
        }
    }

    // Defs are loaded after the mod's constructor, so the window's planned graphics are set up here.
    [StaticConstructorOnStartup]
    public static class WindowPlanSetup
    {
        static WindowPlanSetup()
        {
            WindowsMod.PlannedWindowGraphics();
        }
    }
}
