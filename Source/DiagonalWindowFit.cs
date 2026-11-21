using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Vanilla Diagonal Walls (workshop 3815597808, author Lin) is purely visual: it trims the wall block of a cell into
    // convex polygons so two walls that touch diagonally read as one diagonal run. It exposes the shape it draws:
    //
    //   public static bool Fill(Map map, IntVec3 c, List<Vector2[]> polygons, List<Building> owners, out bool replaces)
    //
    // with the polygons in cell-local coordinates (x east, y north, 0..1) and the building each belongs to. A window in
    // such a cell has to lie along the band instead of square across the cell, so this reads the polygons and gives the
    // drawing code a direction (longest edge), a place (centroid) and a length (extent along that direction).
    //
    // Soft dependency on purpose: bound by reflection the first time it is asked, so the addon keeps working - and
    // windows keep drawing square - when the mod is not installed or its API changes.
    internal static class DiagonalWindowFit
    {
        public struct Fit
        {
            public float yaw;       // degrees, Unity convention: the frame's width runs along this direction
            public float offsetX;   // world offset from the cell centre, east
            public float offsetZ;   // ... north
            public float length;    // extent of the band along that direction, in cells
        }

        private static bool bound;
        private static MethodInfo fill;

        private static readonly List<Vector2[]> polys = new List<Vector2[]>();
        private static readonly List<Building> owners = new List<Building>();

        private static void Bind()
        {
            bound = true;
            try
            {
                Type shapes = AccessTools.TypeByName("DiagonalWalls.DiagonalShapes");
                if (shapes == null)
                {
                    return;
                }
                fill = AccessTools.Method(shapes, "Fill", new[]
                {
                    typeof(Map), typeof(IntVec3), typeof(List<Vector2[]>), typeof(List<Building>),
                    typeof(bool).MakeByRefType()
                });
            }
            catch (Exception)
            {
                fill = null;
            }
        }

        // False: no diagonal wall mod, no wall of ours in the cell, or the cell is not trimmed - draw the window square.
        public static bool TryFit(Map map, IntVec3 c, out Fit fit)
        {
            fit = default(Fit);
            if (map == null || !c.InBounds(map))
            {
                return false;
            }
            if (!bound)
            {
                Bind();
            }
            if (fill == null)
            {
                return false;
            }
            Building wall = c.GetEdifice(map);
            if (wall == null || wall.def == null || !HalfWallState.IsHalfWall(wall.def))
            {
                return false;
            }
            polys.Clear();
            owners.Clear();
            object[] args = { map, c, polys, owners, false };
            try
            {
                fill.Invoke(null, args);
            }
            catch (Exception)
            {
                fill = null;   // its API moved: stop asking rather than log every frame
                return false;
            }
            if (!(args[4] is bool replaces) || !replaces || polys.Count == 0)
            {
                return false;
            }
            return Measure(map, c, wall, out fit);
        }

        private static bool Measure(Map map, IntVec3 c, Building wall, out Fit fit)
        {
            fit = default(Fit);
            // Only the pieces that belong to the wall under the window (the cell may also hold corners of its neighbours).
            Vector2 sum = Vector2.zero;
            int count = 0;
            float bestLength = 0f;
            Vector2 bestDir = Vector2.right;
            for (int p = 0; p < polys.Count; p++)
            {
                if (p < owners.Count && owners[p] != null && owners[p] != wall)
                {
                    continue;
                }
                Vector2[] poly = polys[p];
                if (poly == null || poly.Length < 2)
                {
                    continue;
                }
                for (int i = 0; i < poly.Length; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % poly.Length];
                    Vector2 d = b - a;
                    float len = d.magnitude;
                    if (len > bestLength)
                    {
                        bestLength = len;
                        bestDir = d / len;
                    }
                    sum += a;
                    count++;
                }
            }
            if (count == 0 || bestLength <= 0.01f)
            {
                return false;
            }
            Vector2 centre = sum / count;
            float lo = float.MaxValue;
            float hi = float.MinValue;
            for (int p = 0; p < polys.Count; p++)
            {
                if (p < owners.Count && owners[p] != null && owners[p] != wall)
                {
                    continue;
                }
                Vector2[] poly = polys[p];
                if (poly == null)
                {
                    continue;
                }
                for (int i = 0; i < poly.Length; i++)
                {
                    float proj = Vector2.Dot(poly[i] - centre, bestDir);
                    lo = Mathf.Min(lo, proj);
                    hi = Mathf.Max(hi, proj);
                }
            }
            fit.offsetX = centre.x - 0.5f;
            fit.offsetZ = centre.y - 0.5f;
            // The model's width runs along its own +X; a Unity yaw of t turns +X to (cos t, 0, -sin t).
            fit.yaw = Mathf.Atan2(-bestDir.y, bestDir.x) * Mathf.Rad2Deg;
            fit.length = hi - lo;
            return fit.length > 0.05f;
        }
    }
}
