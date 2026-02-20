using UnityEngine;
using Verse;

namespace MapLevelFramework
{
    public class MLF_Settings : ModSettings
    {
        public bool debugPathfindingAndJob;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref debugPathfindingAndJob, "debugPathfindingAndJob", false);
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled(
                "寻路与job检测日志",
                ref debugPathfindingAndJob,
                "开启后在日志中输出跨层寻路和工作分配的详细信息。格式：【MLF】寻路与job检测-{pawn}—...");
            listing.End();
        }
    }
}
