using System.Collections.Generic;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 存储一个层级地图的内容，用于逆重飞船起飞/降落时保存和恢复。
    /// </summary>
    public class GravshipLevelStorage : IExposable
    {
        public int elevation;
        public CellRect area;
        public bool isUnderground;
        public List<IntVec3> usableCellsList;

        public List<Thing> things = new List<Thing>();
        public List<IntVec3> thingPositions = new List<IntVec3>();
        public List<Rot4> thingRotations = new List<Rot4>();

        public List<Pawn> pawns = new List<Pawn>();
        public List<IntVec3> pawnPositions = new List<IntVec3>();
        public List<Rot4> pawnRotations = new List<Rot4>();

        public Dictionary<IntVec3, TerrainDef> terrains = new Dictionary<IntVec3, TerrainDef>();
        public Dictionary<IntVec3, RoofDef> roofs = new Dictionary<IntVec3, RoofDef>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref elevation, "elevation");
            Scribe_Values.Look(ref area, "area");
            Scribe_Values.Look(ref isUnderground, "isUnderground");
            Scribe_Collections.Look(ref usableCellsList, "usableCells", LookMode.Value);

            Scribe_Collections.Look(ref things, "things", LookMode.Deep);
            Scribe_Collections.Look(ref thingPositions, "thingPositions", LookMode.Value);
            Scribe_Collections.Look(ref thingRotations, "thingRotations", LookMode.Value);

            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
            Scribe_Collections.Look(ref pawnPositions, "pawnPositions", LookMode.Value);
            Scribe_Collections.Look(ref pawnRotations, "pawnRotations", LookMode.Value);

            Scribe_Collections.Look(ref terrains, "terrains", LookMode.Value, LookMode.Def);
            Scribe_Collections.Look(ref roofs, "roofs", LookMode.Value, LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (things == null) things = new List<Thing>();
                if (thingPositions == null) thingPositions = new List<IntVec3>();
                if (thingRotations == null) thingRotations = new List<Rot4>();
                if (pawns == null) pawns = new List<Pawn>();
                if (pawnPositions == null) pawnPositions = new List<IntVec3>();
                if (pawnRotations == null) pawnRotations = new List<Rot4>();
                if (terrains == null) terrains = new Dictionary<IntVec3, TerrainDef>();
                if (roofs == null) roofs = new Dictionary<IntVec3, RoofDef>();
            }
        }
    }
}
