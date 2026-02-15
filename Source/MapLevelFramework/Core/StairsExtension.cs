using Verse;

namespace MapLevelFramework
{
    /// <summary>
    /// 楼梯方向扩展。附加在 ThingDef 上，标记楼梯是上楼还是下楼。
    /// </summary>
    public class StairsExtension : DefModExtension
    {
        public bool goesDown = false;
    }
}
