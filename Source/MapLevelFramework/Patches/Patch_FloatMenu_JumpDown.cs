using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MapLevelFramework.Patches
{
    /// <summary>
    /// 右键菜单 - 在上层露天边缘添加"跳下楼"选项。
    /// 1.6 使用 FloatMenuOptionProvider 模式。
    /// </summary>
    public class FloatMenuProvider_JumpDown : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null || !pawn.IsColonistPlayerControlled) return false;
            if (!LevelManager.IsLevelMap(pawn.Map, out _, out _)) return false;
            return true;
        }

        protected override FloatMenuOption GetSingleOption(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null) return null;
            return JumpDownUtility.GetJumpDownOption(pawn, context.ClickedCell);
        }
    }
}
