using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Optional, and only while HSK's FPS Control Bridge is installed (user, 2026-10-21).
    //
    // That bridge fires its own bullets: it builds a collision box per thing from the thing's fillage and height, and a
    // window is drawn but has no fillage - so in first person its shots went through a shut window and the window never
    // took the hit. Here the bridge is given a box for the opening itself, as much of it as the boards leave covered
    // (the same figures the vanilla shot report uses), and the window becomes the thing the bullet meets.
    //
    // Nothing is referenced at build time: the bridge is found by name, every call into it is guarded, and when it is not
    // installed - or its insides change - the hook simply does not apply.
    public static class FpsBridgeCompat
    {
        private const string Bridge = "HSK.FpsBridge";

        private static bool tried;

        private static bool ready;

        private static MethodInfo boxMethod;

        private static MethodInfo offsetMethod;

        private static Type v3Type;

        private static object solidRegion;

        private static Func<Thing, float> heightOf;

        private static bool logged;

        public static void TryPatch(Harmony harmony)
        {
            Type ballistics = AccessTools.TypeByName(Bridge + ".Ballistics");
            if (ballistics == null)
            {
                return;   // the bridge is not installed: nothing to do
            }
            MethodInfo addSolid = null;
            foreach (MethodInfo m in ballistics.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "AddSolid" && m.GetParameters().Length == 2)
                {
                    addSolid = m;
                    break;
                }
            }
            if (addSolid == null)
            {
                Log.WarningOnce("[ProjectZ] windows: HSK's FPS bridge is installed but its AddSolid method was not found; "
                    + "in first person a window will stay a hole", 0x5A1800);
                return;
            }
            try
            {
                harmony.Patch(addSolid, postfix: new HarmonyMethod(typeof(FpsBridgeCompat), nameof(Postfix)));
            }
            catch (Exception)
            {
                // Older Harmony versions refuse a plain object parameter here; the boxed-arguments form is the fallback.
                try
                {
                    harmony.Patch(addSolid, postfix: new HarmonyMethod(typeof(FpsBridgeCompat), nameof(PostfixArgs)));
                }
                catch (Exception e)
                {
                    Log.WarningOnce("[ProjectZ] windows: could not hook HSK's FPS bridge; in first person a window will "
                        + "stay a hole: " + e, 0x5A1801);
                }
            }
        }

        // The list of hit volumes for one thing, as Harmony hands it over with the boxed parameter.
        public static void Postfix(Thing thing, object into)
        {
            Add(thing, into as IList);
        }

        // Same thing, for Harmony builds that only bind the arguments as an array.
        public static void PostfixArgs(Thing thing, object[] __args)
        {
            if (__args != null && __args.Length > 1)
            {
                Add(thing, __args[1] as IList);
            }
        }

        private static void Add(Thing thing, IList into)
        {
            if (into == null || !Ready())
            {
                return;
            }
            Building_WindowGlass window = thing as Building_WindowGlass;
            if (window == null || !window.Spawned)
            {
                return;
            }
            float covered = Covered(window);
            if (covered <= 0f)
            {
                return;
            }
            object box = Box(window, covered);
            if (box == null)
            {
                return;
            }
            into.Add(box);
            if (!logged)
            {
                logged = true;
                Log.Message("[ProjectZ] windows: HSK's FPS bridge now meets windows (a bullet reaches the window, not the "
                    + "hole it stands in)");
            }
        }

        // How much of the opening the boards leave covered: the same figures the shot report uses, with a shut window
        // covering all of it and a wide open one nothing at all.
        private static float Covered(Building_WindowGlass window)
        {
            if (WindowOrientation.IsSideways(window))
            {
                return 0.5f;
            }
            CompWindowLatch latch = window.Latch;
            float open = latch == null ? 0f : latch.openPct;
            if (open >= 0.999f)
            {
                return 0f;
            }
            if (open >= 0.5f)
            {
                return 0.3f;
            }
            return open > 0.001f ? 0.9f : 1f;
        }

        private static object Box(Building_WindowGlass window, float covered)
        {
            Map map = window.Map;
            if (map == null)
            {
                return null;
            }
            // The opening runs from the top of the half wall to the top of the frame; the bridge's own height reading
            // gives both, so the box lands in its world coordinates exactly where the boards are.
            float bottom = 0f;
            Building wall = HalfWallState.WallAt(window.Position, map);
            if (wall != null)
            {
                bottom = HeightOf(wall);
            }
            float top = HeightOf(window);
            if (top <= bottom + 0.05f)
            {
                top = bottom + 0.8f;
            }
            Vector3 offset = OffsetOf(window);
            float half = 0.5f * Mathf.Sqrt(covered);
            object centre = V3(window.Position.x + 0.5f + offset.x, (bottom + top) * 0.5f + offset.y,
                window.Position.z + 0.5f + offset.z);
            object halfSize = V3(half, (top - bottom) * 0.5f, half);
            try
            {
                return boxMethod.Invoke(null, new[] { solidRegion, centre, halfSize, (object)0f });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[ProjectZ] windows: could not build a hit box for the FPS bridge: " + e, 0x5A1802);
                return null;
            }
        }

        private static object V3(float x, float y, float z)
        {
            return Activator.CreateInstance(v3Type, x, y, z);
        }

        private static float HeightOf(Thing thing)
        {
            if (heightOf == null)
            {
                return 0f;
            }
            try
            {
                return heightOf(thing);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        private static Vector3 OffsetOf(Thing thing)
        {
            if (offsetMethod == null)
            {
                return Vector3.zero;
            }
            try
            {
                return (Vector3)offsetMethod.Invoke(null, new object[] { thing });
            }
            catch (Exception)
            {
                return Vector3.zero;
            }
        }

        // The bridge's box type and helpers, found once and only by name.
        private static bool Ready()
        {
            if (tried)
            {
                return ready;
            }
            tried = true;
            try
            {
                Type volumes = AccessTools.TypeByName(Bridge + ".BodyVolumes");
                Type bindings = AccessTools.TypeByName(Bridge + ".Bindings");
                Type floorSpace = AccessTools.TypeByName(Bridge + ".FloorSpace");
                if (volumes == null || bindings == null || floorSpace == null)
                {
                    return false;
                }
                foreach (MethodInfo m in volumes.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    ParameterInfo[] p = m.GetParameters();
                    if (m.Name == "Box" && p.Length == 4 && p[0].ParameterType.IsEnum
                        && p[1].ParameterType == p[2].ParameterType)
                    {
                        boxMethod = m;
                        v3Type = p[1].ParameterType;
                        solidRegion = Enum.Parse(p[0].ParameterType, "Solid");
                        break;
                    }
                }
                FieldInfo pick = AccessTools.Field(bindings, "PickHeight");
                if (pick != null)
                {
                    heightOf = pick.GetValue(null) as Func<Thing, float>;
                }
                offsetMethod = AccessTools.Method(floorSpace, "Offset", new[] { typeof(Thing) });
                ready = boxMethod != null && v3Type != null && solidRegion != null;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[ProjectZ] windows: the FPS bridge's hit volumes were not understood; in first person a "
                    + "window will stay a hole: " + e, 0x5A1803);
                ready = false;
            }
            return ready;
        }
    }
}
