using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // Which way a window really opens is a question about the wall, not about the window's own turn (user, 2026-10-20).
    // A window can be set across the wall - "sideways" - and then the wall runs along the window's own facing axis, so the
    // two sides of the opening are the cells across the wall instead. Count the open neighbours on both axes and take the
    // axis with more of them: nothing here is a constant, it comes from the room and wall layout around the cell.
    //
    // Windows that come out sideways are a special case everywhere:
    //   - light: always open to the day, whatever the boards do (their slats slide along the wall, so they cover nothing);
    //   - air: always ventilating at a fully open vent's rate, and always leaking vacuum like a fully open window;
    //   - shots: never - the boards are not what stops a bullet there, the wall itself is (user, 2026-10-21).
    public static class WindowOrientation
    {
        public static IntVec3 Axis(Building_WindowGlass window, out bool sideways)
        {
            sideways = false;
            if (window == null)
            {
                return IntVec3.East;
            }
            IntVec3 facing = window.Rotation.FacingCell;
            IntVec3 across = new IntVec3(facing.z, 0, facing.x);
            Map map = window.Map;
            if (map == null)
            {
                return facing;
            }
            sideways = OpenSides(map, window.Position, across) > OpenSides(map, window.Position, facing);
            return sideways ? across : facing;
        }

        public static bool IsSideways(Building_WindowGlass window)
        {
            bool sideways;
            Axis(window, out sideways);
            return sideways;
        }

        // How many of the two cells on this axis are openings: walls, half walls and doors are what the window sits in,
        // furniture is not an edifice and does not count.
        public static int OpenSides(Map map, IntVec3 pos, IntVec3 axis)
        {
            int open = 0;
            if (IsOpening(map, pos + axis))
            {
                open++;
            }
            if (IsOpening(map, pos - axis))
            {
                open++;
            }
            return open;
        }

        public static bool IsOpening(Map map, IntVec3 c)
        {
            return map != null && c.InBounds(map) && c.GetEdifice(map) == null;
        }
    }
}
