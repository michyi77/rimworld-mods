using Verse;

namespace ProjectZ.Windows
{
    // Keeps the daylight a bare half wall lets in up to date. Windows already ask for the light table to be rebuilt when
    // their boards move and now and then for the sun; a bare half wall has no comp of its own, so without this a map with
    // half walls and no windows would never have its light table worked out at all.
    //
    // It does nothing per tick but a hash check, and the table itself throttles any number of these to one rebuild a
    // second (WindowLight.RequestRefresh).
    public class CompProperties_HalfWallLight : CompProperties
    {
        public CompProperties_HalfWallLight()
        {
            compClass = typeof(CompHalfWallLight);
        }
    }

    public class CompHalfWallLight : ThingComp
    {
        private const int RefreshTicks = 60;

        public override void CompTick()
        {
            base.CompTick();
            if (parent.IsHashIntervalTick(RefreshTicks))
            {
                WindowLight.RequestRefresh(parent.Map);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            WindowLight.RequestRebuild(parent.Map);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            WindowLight.RequestRebuild(map);
        }
    }
}
