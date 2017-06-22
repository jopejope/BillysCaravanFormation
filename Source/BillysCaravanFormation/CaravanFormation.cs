using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;
using Harmony;
using System.Reflection;

namespace BillysCaravanFormation
{
    public class LordToil_BillyCaravan_GatherItemsAndPackAnimals : LordToil_PrepareCaravan_GatherAnimals
    {
        // Improvements for this class:
        //   - Animals head to the caravan spot on their own, no need to be tagged by a colonist
        //   - Colonists can start gathering items immediately without waiting for the animals to gather
        //   - Only waits for pack animals before moving on to the next step

        protected virtual IntVec3 MeetingPoint
        {
            get
            {
                return (data as LordToilData_BillyCaravan_GatherPawns).meetingPoint;
            }
            set
            {
                (data as LordToilData_BillyCaravan_GatherPawns).meetingPoint = value;
                (data as LordToilData_BillyCaravan_GatherPawns).gatheredPawns.Clear();
            } 
        }

        public LordToil_BillyCaravan_GatherItemsAndPackAnimals(IntVec3 meetingPoint) : base(meetingPoint)
        {
            this.data = new LordToilData_BillyCaravan_GatherPawns(meetingPoint);
        }

        static bool IsPawnToBeGathered(Pawn p)
        {
            return p.RaceProps.Animal && p.RaceProps.packAnimal;
        }

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < this.lord.ownedPawns.Count; i++)
            {
                Pawn pawn = this.lord.ownedPawns[i];
                if (pawn.IsColonist)
                {
                    // colonists can start gathering items right away
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_GatherItems);
                }
                else if (pawn.RaceProps.Animal && pawn.RaceProps.packAnimal)
                {
                    // pack animals goto meeting point
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Travel, MeetingPoint, -1f);
                    pawn.mindState.duty.locomotion = LocomotionUrgency.Jog;
                }
                else if (!pawn.RaceProps.Animal)
                {
                    // prisoners wait (they will be gathered in a later step)
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_Wait);
                }
                else
                {
                    // other pawns go wait at the meeting point
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_Wait, MeetingPoint, -1f);
                }
            }
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 100 == 0)
            {
                (data as LordToilData_BillyCaravan_GatherPawns).CheckGathered(lord, "AllAnimalsGathered", IsPawnToBeGathered, false);
            }
        }
    }

    public class LordToil_BillyCaravan_GatherSlaves : LordToil_PrepareCaravan_GatherSlaves
    {
        // Improvements for this class:
        //  - Uses the GatherPawns data to keep track of prisoners that have been gathered,
        //    so that if a prisoner wanders off to get food it will still count as gathered
        //    without having to wait for all of them to reach the meeting spot at once.

        protected virtual IntVec3 MeetingPoint
        {
            get
            {
                return (data as LordToilData_BillyCaravan_GatherPawns).meetingPoint;
            }
            set
            {
                (data as LordToilData_BillyCaravan_GatherPawns).meetingPoint = value;
                (data as LordToilData_BillyCaravan_GatherPawns).gatheredPawns.Clear();
            } 
        }

        public LordToil_BillyCaravan_GatherSlaves(IntVec3 meetingPoint) : base(meetingPoint)
        {
            this.data = new LordToilData_BillyCaravan_GatherPawns(meetingPoint);
        }

        static bool IsPawnToBeGathered(Pawn p)
        {
            return !p.IsColonist && !p.RaceProps.Animal;
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 100 == 0)
            {
                (data as LordToilData_BillyCaravan_GatherPawns).CheckGathered(lord, "AllSlavesGathered", IsPawnToBeGathered, true);
            }
        }
    }

    public class LordToilData_BillyCaravan_GatherPawns : LordToilData
    {
        // This keeps a list of all pawns that have successfully made it to the destination meeting point.
        // This is important because after a pawn has been gathered it might wander off for some reason (usually to get food)
        // and while it is gone then another pawn will wander off, and so on, preventing the caravan from ever
        // forming. Instead, we keep track of the pawns that have been gathered and still count them as gathered
        // even if they wander off temporarily.
        public List<Pawn> gatheredPawns;
        public IntVec3 meetingPoint;

        public LordToilData_BillyCaravan_GatherPawns(IntVec3 meetingPoint_)
        {
            this.gatheredPawns = new List<Pawn>();
            this.meetingPoint = meetingPoint_;
        }

        public LordToilData_BillyCaravan_GatherPawns()
        {
            this.gatheredPawns = new List<Pawn>();
            this.meetingPoint = IntVec3.Invalid;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look<Pawn>(ref gatheredPawns, "gatheredPawns", LookMode.Reference, new object[0]);
            Scribe_Values.Look<IntVec3>(ref this.meetingPoint, "meetingPoint", default(IntVec3), false);
        }

        protected bool AllGathered(List<Pawn> pawns, Predicate<Pawn> shouldCheck)
        {
            bool flag = true;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (shouldCheck(pawn) && !gatheredPawns.Contains(pawn))
                {
                    flag = false;
                    break;
                }
            }

            return flag;
        }

        protected void SetGathered(Pawn p)
        {
            if (!gatheredPawns.Contains(p))
            {
                gatheredPawns.Add(p);
            }
        }

        public void CheckGathered(Lord lord, string memo, Predicate<Pawn> shouldCheck, bool validateFollow)
        {
            // this checks if all pawns have been gathered. It is analogous to GatherAnimalsAndSlavesForCaravanUtility.CheckGathered
            // except that it keeps track of pawns that have reached their destination and keeps counting them as gathered
            // even if they wander off to get food.
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (shouldCheck(pawn) && 
                    (!validateFollow || GatherAnimalsAndSlavesForCaravanUtility.IsFollowingAnyone(pawn)) && 
                    pawn.Position.InHorDistOf(meetingPoint, 10f) && 
                    pawn.CanReach(meetingPoint, PathEndMode.ClosestTouch, Danger.Deadly, false, TraverseMode.ByPawn))
                {
                    SetGathered(pawn);
                }
            }
            if (AllGathered(lord.ownedPawns, shouldCheck))
            {
                lord.ReceiveMemo(memo);
            }
        }
    }

    public static class ExtendLordJob_FormAndSendCaravan
    {
        public static bool StillHasEnoughCapacity(this LordJob_FormAndSendCaravan self)
        {
            // here we check the capacity of the caravan after a pawn is lost. We have to count all the stuff still waiting to be loaded
            // plus the stuff already loaded. Note that the caravan dialog code uses IgnoreIfAssignedToUnload to count loaded inventory
            // but I think that's a mistake because it allows you to create overloaded caravans by loading some stuff, then cancelling,
            // then forming the caravan again.
            float massLeftToTransfer = CollectionsMassCalculator.MassUsageTransferables(self.transferables, IgnorePawnsInventoryMode.DontIgnore, false, false);
            float massAlreadyTransfered = CollectionsMassCalculator.MassUsage(self.lord.ownedPawns, IgnorePawnsInventoryMode.DontIgnore, false, false);
            float totalMass = massLeftToTransfer + massAlreadyTransfered;
            float totalCapacity = CollectionsMassCalculator.Capacity(self.lord.ownedPawns);
            //Debug.Log("Checking Caravan Capacity: " + totalMass + "/" + totalCapacity);
            return totalMass <= totalCapacity;
        }

        public static bool StillHasColonist(this LordJob_FormAndSendCaravan self)
        {
            foreach (Pawn p in self.lord.ownedPawns)
            {
                if (p.IsColonist) return true;
            }
            return false;
        }
    }

    public class JobGiver_WanderPrisonCell : JobGiver_WanderCurrentRoom
    {
        // This class replaces JobGiver_WanderCurrentRoom for prisoners in caravans in the caravan duties defs.
        // It is added to the game by PatchCaravan.xml in the Patches folder.
        protected override IntVec3 GetExactWanderDest(Pawn pawn)
        {
            // the class JobGiver_WanderCurrentRoom is not very strict about keeping prisoners in their cells.
            // this one makes sure the wander locations really stay within the cell
            IntVec3 wanderRoot = this.GetWanderRoot(pawn);
            IntVec3 wanderDest = wanderRoot;
            for (int i = 0; i < 20; i++)
            {
                wanderDest = RCellFinder.RandomWanderDestFor(pawn, wanderRoot, this.wanderRadius, this.wanderDestValidator, PawnUtility.ResolveMaxDanger(pawn, this.maxDanger));
                bool destOk = WanderRoomUtility.IsValidWanderDest(pawn, wanderDest, wanderRoot);
                if (DebugViewSettings.drawDestSearch)
                {
                    pawn.Map.debugDrawer.FlashCell(wanderRoot, 0.6f, "PC root");
                    if (destOk)
                    {
                        pawn.Map.debugDrawer.FlashCell(wanderDest, 0.9f, "dest OK");
                    }
                    else
                    {
                        pawn.Map.debugDrawer.FlashCell(wanderDest, 0.9f, "dest BAD");
                    }
                }
                if (destOk) return wanderDest;
            }
            // if we haven't found a good destination by now, probably the prisoner is in a very small cell and doesn't have room to wander.
            return wanderRoot;
        }
    }

    [HarmonyPatch(typeof(LordJob_FormAndSendCaravan))]
    [HarmonyPatch("CreateGraph")]
    public class PatchLordJob_FormAndSendCaravan_CreateGraph
    {
        [HarmonyPostfix]
        static void Postfix(LordJob_FormAndSendCaravan __instance, ref StateGraph __result)
        {
            // This patch modifies the return value of LordJob_FormAndSendCaravan.CreateGraph
            // The CreateGraph function generates a finite state machine that controls caravan formation.
            // Each state is a LordToil.
            // We need to make a few changes to that state machine.

            Traverse tthis = Traverse.Create(__instance);
            IntVec3 meetingPoint = tthis.Field("meetingPoint").GetValue<IntVec3>();

            // here we create new states to add to the state machine.
            // We're adding three new caravan forming states, along with three corrisponding pause states (see below)
            // these states will replace the corrisponding states in the original state machine.
            LordToil_BillyCaravan_GatherItemsAndPackAnimals gatherpackanimals = new LordToil_BillyCaravan_GatherItemsAndPackAnimals(meetingPoint);
            LordToil_PrepareCaravan_Pause pause_gatherpackanimals = new LordToil_PrepareCaravan_Pause();
            LordToil_PrepareCaravan_GatherItems gatheritems = new LordToil_PrepareCaravan_GatherItems(meetingPoint);
            LordToil_PrepareCaravan_Pause pause_gatheritems = new LordToil_PrepareCaravan_Pause();
            LordToil_BillyCaravan_GatherSlaves gatherslaves = new LordToil_BillyCaravan_GatherSlaves(meetingPoint);
            LordToil_PrepareCaravan_Pause pause_gatherslaves = new LordToil_PrepareCaravan_Pause();

            // we have to set this variable so the game will know when we're in the Gathering Items phase.
            // sometimes while in this phase it likes to let other colonists (not assigned to the caravan) help with the loading process.
            tthis.Field("gatherItems").SetValue(gatheritems);
           
            // We put our gatherpackanimals state as the new starting state. It will transition to our other states.
            // There is no need to remove the original states, they will now simply be unreachable.
            __result.StartingToil = gatherpackanimals;
            __result.AddToil(gatheritems);
            __result.AddToil(gatherslaves);
            __result.AddToil(pause_gatherpackanimals);
            __result.AddToil(pause_gatheritems);
            __result.AddToil(pause_gatherslaves);

            // adds the new states to the transition that ends the caravan forming process when an important pawn is incapacitated
            Transition endIfPawnLost = __result.transitions[0];
            endIfPawnLost.AddSource(gatherpackanimals);
            endIfPawnLost.AddSource(pause_gatherpackanimals);
            endIfPawnLost.AddSource(gatheritems);
            endIfPawnLost.AddSource(pause_gatheritems);
            endIfPawnLost.AddSource(gatherslaves);
            endIfPawnLost.AddSource(pause_gatherslaves);
            
            // this replaces the failure condition from losing any pawn to only fail if the caravan lacks sufficient capacity or has no colonists
            endIfPawnLost.triggers.Clear();
            endIfPawnLost.AddTrigger(new Trigger_Memo("BillyCaravanCriticalPawnLost"));

            // here we insert the transitions for our new states.
            // the general order of operations is:
            // 1. gather Animals
            // 2. gather Items
            // 3. gather Prisoners
            // 4. wait for every member of the caravan to be sufficiently rested
            // 5. walk to the edge of the map and leave
            // the last two steps are already in the original StateGraph, so we just have to add the first three and their transitions here
            // The order above is the same as in the base game, we're simply replacing steps 1 and 3 with our improved versions.
            Transition doneAnimals = new Transition(gatherpackanimals, gatheritems);
            doneAnimals.AddTrigger(new Trigger_Memo("AllAnimalsGathered"));
            Transition doneItems = new Transition(gatheritems, gatherslaves);
            doneItems.AddTrigger(new Trigger_Memo("AllItemsGathered"));
            Transition doneSlaves = new Transition(gatherslaves, __result.lordToils.Find((LordToil lt) => lt is LordToil_PrepareCaravan_Wait));
            doneSlaves.AddTrigger(new Trigger_Memo("AllSlavesGathered"));
            __result.AddTransition(doneAnimals);
            __result.AddTransition(doneItems);
            __result.AddTransition(doneSlaves);

            // These transitions pause the caravan if a colonist or animal has a mental break.
            // Every caravan forming state has a corrisponding pause state, so that the state machine can remember where it was
            // when it unpauses. We had to create pause-states for each of our new states, and here we add the transitions.
            __result.AddTransition(tthis.Method("PauseTransition", new object[] { gatherpackanimals, pause_gatherpackanimals }).GetValue<Transition>());
            __result.AddTransition(tthis.Method("UnpauseTransition", new object[] { pause_gatherpackanimals, gatherpackanimals }).GetValue<Transition>());
            __result.AddTransition(tthis.Method("PauseTransition", new object[] { gatheritems, pause_gatheritems } ).GetValue<Transition>());
            __result.AddTransition(tthis.Method("UnpauseTransition", new object[] { pause_gatheritems, gatheritems }).GetValue<Transition>());
            __result.AddTransition(tthis.Method("PauseTransition", new object[] { gatherslaves, pause_gatherslaves }).GetValue<Transition>());
            __result.AddTransition(tthis.Method("UnpauseTransition", new object[] { pause_gatherslaves, gatherslaves }).GetValue<Transition>());

            // Set autoFlee to false for the player faction, otherwise the caravan "flees" if it loses half its members.
            if (__instance.lord.faction.def.isPlayer) __instance.lord.faction.def.autoFlee = false;
        }
    }

    [HarmonyPatch(typeof(LordJob_FormAndSendCaravan))]
    [HarmonyPatch("Notify_PawnLost")]
    public class PatchLordJob_FormAndSendCaravan_Notify_PawnLost
    {
        [HarmonyPrefix]
        static void Prefix(LordJob_FormAndSendCaravan __instance, Pawn p, PawnLostCondition condition)
        {
            // This function checks whether the caravan can continue without a downed pawn.
            // We're lucky that the original LordJob_FormAndSendCaravan overrode the Notify_PawnLost function,
            // so we're able to patch it with this code.
            Traverse tthis = Traverse.Create(__instance);
            bool caravanSent = tthis.Field("caravanSent").GetValue<bool>();

            if (!caravanSent)
            {
                string genderpronounsubj = p.gender == Gender.Male ? "Prohe".Translate() : p.gender == Gender.Female ? "Proshe".Translate() : "Proit".Translate();
                string genderpronounobj = p.gender == Gender.Male ? "ProhimObj".Translate() : p.gender == Gender.Female ? "ProherObj".Translate() : "ProitObj".Translate();
                if (!__instance.StillHasColonist())
                {
                    Messages.Message("BillyCaravanLacksColonist".Translate(new object[] { p.NameStringShort, genderpronounsubj, genderpronounobj }), p, MessageSound.Negative);
                    __instance.lord.ReceiveMemo("BillyCaravanCriticalPawnLost");
                }
                else if (__instance.StillHasEnoughCapacity())
                {
                    
                    Messages.Message("BillyCaravanLeavesWithout".Translate(new object[] { p.NameStringShort, genderpronounsubj, genderpronounobj }), p, MessageSound.Negative);
                }
                else
                {
                    
                    Messages.Message("BillyCaravanLacksCapacity".Translate(new object[] { p.NameStringShort, genderpronounsubj, genderpronounobj }), p, MessageSound.Negative);
                    __instance.lord.ReceiveMemo("BillyCaravanCriticalPawnLost");
                }
            }
        }
    }

    public class CaravanModInit : Verse.Mod
    {
        public CaravanModInit(ModContentPack content)
            : base(content)
        {
            // this should be executed when Rimworld loads our mod (because the class inherits from Verse.Mod). 
            // The code below tells Harmony to apply the patches attributed above.
            var harmony = HarmonyInstance.Create("bem.rimworld.mod.billy.smartcaravan");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
        }
    }
}
