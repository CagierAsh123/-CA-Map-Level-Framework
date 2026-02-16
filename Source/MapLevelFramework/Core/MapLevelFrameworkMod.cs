using HarmonyLib;
using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// Map Level Framework - 地图层级框架
    /// 为 RimWorld 提供垂直维度扩展能力：多层建筑、地下、天空、水底等。
    ///
    /// 核心思路（参照 VMF）：
    /// - 每个层级 = 一张真实的 PocketMap，拥有完整原版系统
    /// - 交互重定向：聚焦某层时，鼠标/建造/选择全部指向该层的 Map
    /// - 叠加渲染：子地图内容通过坐标变换画在主地图的指定区域上
    /// </summary>
    public class MapLevelFrameworkMod : Mod
    {
        public static MapLevelFrameworkMod Instance { get; private set; }
        public static Harmony HarmonyInstance { get; private set; }

        public MapLevelFrameworkMod(ModContentPack content) : base(content)
        {
            Instance = this;
            HarmonyInstance = new Harmony("CA.MapLevelFramework");
            HarmonyInstance.PatchAll();

            // 手动 patch internal 类
            PatchInternalClasses();

            Log.Message("[MLF] Initialized.");
        }

        private void PatchInternalClasses()
        {
            // SectionLayer_SunShadows 是 internal 类，无法用 [HarmonyPatch] 属性
            try
            {
                var sunShadowType = AccessTools.TypeByName("Verse.SectionLayer_SunShadows");
                if (sunShadowType == null)
                {
                    Log.Warning("[MLF] SectionLayer_SunShadows type not found.");
                    return;
                }

                // Patch Regenerate - 重新生成时跳过层级区域内的建筑阴影
                var regenerate = AccessTools.Method(sunShadowType, "Regenerate");
                if (regenerate != null)
                {
                    HarmonyInstance.Patch(regenerate,
                        prefix: new HarmonyMethod(AccessTools.Method(
                            typeof(Patches.Patch_SunShadows), nameof(Patches.Patch_SunShadows.Prefix))));
                    Log.Message("[MLF] Patched SectionLayer_SunShadows.Regenerate OK.");
                }

                // Patch DrawLayer - 绘制时跳过与层级区域重叠的 section 的阴影
                var drawLayer = AccessTools.Method(sunShadowType, "DrawLayer");
                if (drawLayer != null)
                {
                    HarmonyInstance.Patch(drawLayer,
                        prefix: new HarmonyMethod(AccessTools.Method(
                            typeof(Patches.Patch_SunShadows), nameof(Patches.Patch_SunShadows.DrawLayerPrefix))));
                    Log.Message("[MLF] Patched SectionLayer_SunShadows.DrawLayer OK.");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[MLF] Failed to patch SunShadows: {ex}");
            }

            // SectionLayer_Zones 是 internal 类 - 聚焦层级时隐藏基地图 zone
            try
            {
                Patches.Patch_ZoneLayer.Apply(HarmonyInstance);
                Log.Message("[MLF] Patched SectionLayer_Zones.Regenerate OK.");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[MLF] Failed to patch ZoneLayer: {ex}");
            }
        }
    }
}
