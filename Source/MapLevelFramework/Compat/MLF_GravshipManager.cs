using System;
using System.Collections.Generic;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// GameComponent - 在逆重飞船飞行期间持久化层级地图数据。
    /// </summary>
    public class MLF_GravshipManager : GameComponent
    {
        private List<GravshipLevelStorage> storedLevels = new List<GravshipLevelStorage>();

        public List<GravshipLevelStorage> StoredLevels => storedLevels;

        public MLF_GravshipManager(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref storedLevels, "mlf_storedLevels", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && storedLevels == null)
                storedLevels = new List<GravshipLevelStorage>();
        }
    }
}
