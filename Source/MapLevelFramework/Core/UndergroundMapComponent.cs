using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 地下层子地图的 MapComponent。
    /// 当 CurrentMap 是地下层子地图时，绘制层级切换 UI。
    /// 在 GenerateLevelMap 中添加到地下层子地图。
    /// </summary>
    public class UndergroundMapComponent : MapComponent
    {
        private LevelManager hostManager;

        public UndergroundMapComponent(Map map) : base(map) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // 恢复 hostManager 引用
            if (map?.Parent is LevelMapParent lmp)
            {
                hostManager = lmp.hostManager;
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (hostManager != null)
            {
                Gui.LevelSwitcherUI.DrawLevelSwitcher(hostManager);
            }
        }
    }
}
