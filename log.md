Called MaterialsNeededTotal on a Blueprint\_Install.

UnityEngine.StackTraceUtility:ExtractStackTrace ()

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.Log.Error\_Patch4 (string)

RimWorld.Blueprint\_Install:TotalMaterialCost ()

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:DigitalStorage.HarmonyPatches.Patch\_WorkGiver\_ConstructDeliverResources\_V2.Postfix\_Patch1 (Verse.AI.Job\&,RimWorld.WorkGiver\_ConstructDeliverResources,Verse.Pawn,RimWorld.IConstructible,bool,bool)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:RimWorld.WorkGiver\_ConstructDeliverResources.ResourceDeliverJobFor\_Patch2 (RimWorld.WorkGiver\_ConstructDeliverResources,Verse.Pawn,RimWorld.IConstructible,bool,bool)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:RimWorld.WorkGiver\_ConstructDeliverResourcesToBlueprints.JobOnThing\_Patch2 (RimWorld.WorkGiver\_ConstructDeliverResourcesToBlueprints,Verse.Pawn,Verse.Thing,bool)

RimWorld.WorkGiver\_Scanner:HasJobOnThing (Verse.Pawn,Verse.Thing,bool)

RimWorld.JobGiver\_Work/<>c\_\_DisplayClass3\_1:<TryIssueJobPackage>g\_\_Validator|0 (Verse.Thing)

Verse.RegionProcessorClosestThingReachable:ProcessThing (Verse.Region,Verse.Thing)

Verse.RegionProcessorClosestThingReachable:RegionProcessor (Verse.Region)

Verse.RegionTraverser/BFSWorker:BreadthFirstTraverseWork (Verse.Region,Verse.RegionEntryPredicate,Verse.RegionProcessor,int,Verse.RegionType)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.RegionTraverser.BreadthFirstTraverse\_Patch1 (Verse.Region,Verse.RegionEntryPredicate,Verse.RegionProcessor,int,Verse.RegionType)

Verse.RegionTraverser:BreadthFirstTraverse (Verse.Region,Verse.RegionProcessorDelegateCache,int,Verse.RegionType)

Verse.GenClosest:RegionwiseBFSWorker (Verse.IntVec3,Verse.Map,Verse.ThingRequest,Verse.AI.PathEndMode,Verse.TraverseParms,System.Predicate`1<Verse.Thing>,System.Func`2<Verse.Thing, single>,int,int,single,int\&,Verse.RegionType,bool,bool)

Verse.GenClosest:ClosestThingReachable (Verse.IntVec3,Verse.Map,Verse.ThingRequest,Verse.AI.PathEndMode,Verse.TraverseParms,single,System.Predicate`1<Verse.Thing>,System.Collections.Generic.IEnumerable`1<Verse.Thing>,int,int,bool,Verse.RegionType,bool,bool)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:RimWorld.JobGiver\_Work.TryIssueJobPackage\_Patch2 (RimWorld.JobGiver\_Work,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult RimWorld.JobGiver\_Work:TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams),Verse.AI.ThinkResult RimWorld.JobGiver\_Work.TryIssueJobPackage\_Patch2(RimWorld.JobGiver\_Work, Verse.Pawn, Verse.AI.JobIssueParams)> (RimWorld.JobGiver\_Work,Verse.AI.ThinkResult\&,Verse.Pawn,Verse.AI.JobIssueParams)

MapLevelFramework.Patches.Patch\_CrossLevelJobScan/<>c\_\_DisplayClass0\_0:<Postfix>b\_\_0 ()

MapLevelFramework.CrossLevelJobUtility:TryCrossLevelScan (Verse.Pawn,System.Func`1<Verse.AI.Job>)

MapLevelFramework.Patches.Patch\_CrossLevelJobScan:Postfix (Verse.AI.ThinkResult\&,RimWorld.JobGiver\_Work,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:RimWorld.JobGiver\_Work.TryIssueJobPackage\_Patch2 (RimWorld.JobGiver\_Work,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult RimWorld.JobGiver\_Work:TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams),Verse.AI.ThinkResult RimWorld.JobGiver\_Work.TryIssueJobPackage\_Patch2(RimWorld.JobGiver\_Work, Verse.Pawn, Verse.AI.JobIssueParams)> (RimWorld.JobGiver\_Work,Verse.AI.ThinkResult\&,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.ThinkNode\_PrioritySorter.TryIssueJobPackage\_Patch1 (Verse.AI.ThinkNode\_PrioritySorter,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult Verse.AI.ThinkNode\_PrioritySorter:TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams),Verse.AI.ThinkResult Verse.AI.ThinkNode\_PrioritySorter.TryIssueJobPackage\_Patch1(Verse.AI.ThinkNode\_PrioritySorter, Verse.Pawn, Verse.AI.JobIssueParams)> (Verse.AI.ThinkNode\_PrioritySorter,Verse.AI.ThinkResult\&,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.ThinkNode\_Priority.TryIssueJobPackage\_Patch1 (Verse.AI.ThinkNode\_Priority,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult Verse.AI.ThinkNode\_Priority:TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams),Verse.AI.ThinkResult Verse.AI.ThinkNode\_Priority.TryIssueJobPackage\_Patch1(Verse.AI.ThinkNode\_Priority, Verse.Pawn, Verse.AI.JobIssueParams)> (Verse.AI.ThinkNode\_Priority,Verse.AI.ThinkResult\&,Verse.Pawn,Verse.AI.JobIssueParams)

Verse.AI.ThinkNode\_Tagger:TryIssueJobPackage (Verse.Pawn,Verse.AI.JobIssueParams)

Verse.AI.ThinkNode\_Subtree:TryIssueJobPackage (Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.ThinkNode\_Priority.TryIssueJobPackage\_Patch1 (Verse.AI.ThinkNode\_Priority,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult Verse.AI.ThinkNode\_Priority:TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams),Verse.AI.ThinkResult Verse.AI.ThinkNode\_Priority.TryIssueJobPackage\_Patch1(Verse.AI.ThinkNode\_Priority, Verse.Pawn, Verse.AI.JobIssueParams)> (Verse.AI.ThinkNode\_Priority,Verse.AI.ThinkResult\&,Verse.Pawn,Verse.AI.JobIssueParams)

Verse.AI.ThinkNode\_Conditional:TryIssueJobPackage (Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.ThinkNode\_Priority.TryIssueJobPackage\_Patch1 (Verse.AI.ThinkNode\_Priority,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult Verse.AI.ThinkNode\_Priority:TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams),Verse.AI.ThinkResult Verse.AI.ThinkNode\_Priority.TryIssueJobPackage\_Patch1(Verse.AI.ThinkNode\_Priority, Verse.Pawn, Verse.AI.JobIssueParams)> (Verse.AI.ThinkNode\_Priority,Verse.AI.ThinkResult\&,Verse.Pawn,Verse.AI.JobIssueParams)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.Pawn\_JobTracker.DetermineNextJob\_Patch2 (Verse.AI.Pawn\_JobTracker,Verse.ThinkTreeDef\&,bool)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Glue:AbiFixup<Verse.AI.ThinkResult Verse.AI.Pawn\_JobTracker:DetermineNextJob(Verse.ThinkTreeDef\&, System.Boolean),Verse.AI.ThinkResult Verse.AI.Pawn\_JobTracker.DetermineNextJob\_Patch2(Verse.AI.Pawn\_JobTracker, Verse.ThinkTreeDef\&, System.Boolean)> (Verse.AI.Pawn\_JobTracker,Verse.AI.ThinkResult\&,Verse.ThinkTreeDef\&,bool)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.Pawn\_JobTracker.TryFindAndStartJob\_Patch1 (Verse.AI.Pawn\_JobTracker)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.AI.Pawn\_JobTracker.EndCurrentJob\_Patch4 (Verse.AI.Pawn\_JobTracker,Verse.AI.JobCondition,bool,bool)

Verse.AI.Pawn\_JobTracker:JobTrackerTickInterval (int)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.Pawn.TickInterval\_Patch1 (Verse.Pawn,int)

Verse.Thing:DoTick ()

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.TickList.Tick\_Patch3 (Verse.TickList)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.TickManager.DoSingleTick\_Patch5 (Verse.TickManager)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.TickManager.TickManagerUpdate\_Patch3 (Verse.TickManager)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.Game.UpdatePlay\_Patch3 (Verse.Game)

(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:Verse.Root\_Play.Update\_Patch1 (Verse.Root\_Play)



搬运建筑去其他楼层

