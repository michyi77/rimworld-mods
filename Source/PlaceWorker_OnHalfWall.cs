using RimWorld;
using Verse;

namespace ProjectZ.Windows
{
    // A window may only be placed on a half wall.
    //
    // The window takes the half wall's cell: the half wall is what makes a wall "cut down", and the window is the
    // glazed version of that. So the cell has to hold a PZ_HalfWall first.
    // building.canPlaceOverWall on the window def lets the blueprint sit on the occupied cell; the
    // GenSpawn.SpawningWipes patch in this mod keeps the wall when the window is finished.
    public class PlaceWorker_OnHalfWall : PlaceWorker
    {
        private const string HalfWallDefName = "PZ_HalfWall";

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            Thing edifice = loc.GetEdifice(map);
            if (edifice != null && HalfWallState.IsHalfWall(edifice.def))
            {
                return AcceptanceReport.WasAccepted;
            }
            return new AcceptanceReport("A window has to go on a half wall: build one in this cell first.");
        }
    }
}
