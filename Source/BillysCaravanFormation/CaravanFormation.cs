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
    /*
     * WTF is going on here?
     * 
     * Fundamentally this mod is replacing some of the native Caravan Formation code in Rimworld.
     * For every class with code that I am replacing, I have written a class (below) that inherits from the
     * class that I want to replace. Elsewhere, I will tell Rimworld to use my classes instead of the original
     * classes. Because I'm inheriting from the original classes, I only have to write the code that I am modifying.
     * Everything else will get 'inherited' from the original class.
     * 
     * The first line of the class tells you what original class I am inheriting from, and therefore what class I
     * am modifying/replacing. C# uses the colon : to denote inheritance (just like in C++). So for example this first
     * class is modifying and replacing LordToil_PrepareCaravan_GatherAnimals. You will have to use ILSpy to decompile
     * the Rimworld code in "Assembly-CSharp.dll" and then search for "LordToil_PrepareCaravan_GatherAnimals" to find
     * the original code and see how it works. In fact, you can search the original code for just "Caravan" and get nearly
     * all the code that relates to caravan formation. The decompiled source code is surprisingly easy to read and
     * understand so it won't take too long to get a basic understanding of what is going on.
     * 
     * The first thing you will have to learn is what the hell is a "LordToil". One thing that is a bit confusing about
     * the Rimworld source is that the developers like to use basically synonomous words to refer to completely different
     * things related to the same overall concept. There are LordToils and LordJobs, for example. Totally different. What
     * is a LordJob? Let's not worry about that right now.
     * 
     * A LordToil is a state that a 'Lord' can be in that describes what the Lord should be doing at this very moment. A Lord
     * orchistrates a group of pawns as they do some complex job together (such as forming a caravan). the Lord will switch from 
     * one LordToil to the next, according to a state machine that describes how to do the whole job. So each LordToil is one
     * state in this state machine, and the pawns must keep working on the same LordToil until it tells the Lord to transition
     * to the next state.
     *
     * The LordToil has a function "UpdateAllDuties" which tells all the Pawns controlled by the Lord what they need to do.
     * This is done by assigning each pawn a PawnDuty. What is a PawnDuty? Well, we don't have to worry about that, because
     * we don't need to be modifying any code at that level. The individual duties for pawns during caravan formation work
     * great. The only reason caravan formation breaks is because the Lord gets stuck in certain states and doesn't know when
     * to move on to the next state. All we have to do is fix the LordToils (the states) so that they can proceed smoothly
     * even when forming a large caravan.
     * 
     * The LordToil also has a function "LordToilTick" that checks often if we are done with this Toil and can move on to
     * the next state. This is where caravan formation would often break as a toil would wait for all pawns to be close to
     * the meeting point, but the problem is pawns would get hungry and wander off after getting to the meeting point, and
     * then while that was happening another pawn would get hungry, and so on, so the caravan would never move on to the
     * next step.
     * 
     * We fix this by adding a "LordToilData" to each LordToil that has to wait for all pawns to gather. Instead of waiting
     * for every pawn to be in position at the same time, it simply tracks when a pawn has made it to the meeting point, and
     * if that has happened ONCE for each pawn then that is sufficient for us to move on. Now, if a hungry pawn wanders off,
     * it doesn't delay the caravan since that pawn is already counted as having been at the meeting point.
     */

    public class LordToil_BillyCaravan_GatherAnimals : LordToil_PrepareCaravan_GatherAnimals
    {
        // Improvements for this class:
        //   - Tracks gathered pawns with LordToilData so pawns that wander off for food will not delay the caravan
        //   - This step can be skipped with the fastAnimalCollection setting.

        static bool IsFastAnimalCollectionEnabled
        {
            get
            {
                return LoadedModManager.GetMod<CaravanModInit>().GetSettings<CaravanModSettings>().fastAnimalCollection;
            }
        }

        public virtual IntVec3 MeetingPoint
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

        public LordToil_BillyCaravan_GatherAnimals(IntVec3 meetingPoint) : base(meetingPoint)
        {
            this.data = new LordToilData_BillyCaravan_GatherPawns(meetingPoint);
        }

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn.IsColonist || pawn.RaceProps.Animal)
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_GatherPawns, MeetingPoint);
                    pawn.mindState.duty.pawnsToGather = PawnsToGather.Animals;
                }
                else
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_Wait);
                }
            }
        }

        public override void LordToilTick()
        {
            const string toilCompleteMemo = "AllAnimalsGathered";
            if (Find.TickManager.TicksGame % 100 == 0)
            {
                if (IsFastAnimalCollectionEnabled)
                {
                    lord.ReceiveMemo(toilCompleteMemo);
                }
                else
                {
                    (data as LordToilData_BillyCaravan_GatherPawns).CheckGathered(lord, toilCompleteMemo, (Pawn x) => x.RaceProps.Animal, true);
                }
            }
        }
    }

    public class LordToil_BillyCaravan_GatherSlaves : LordToil_PrepareCaravan_GatherSlaves
    {
        // Improvements for this class:
        //   - Tracks gathered pawns with LordToilData so pawns that wander off for food will not delay the caravan

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

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (!pawn.RaceProps.Animal)
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_GatherPawns, MeetingPoint);
                    pawn.mindState.duty.pawnsToGather = PawnsToGather.Slaves;
                }
                else
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_Wait, MeetingPoint);
                }
            }
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 100 == 0)
            {
                (data as LordToilData_BillyCaravan_GatherPawns).CheckGathered(lord, "AllSlavesGathered", (Pawn x) => !x.IsColonist && !x.RaceProps.Animal, true);
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
                    pawn.Spawned &&
                    pawn.Position.InHorDistOf(meetingPoint, 10f) &&
                    pawn.CanReach(meetingPoint, PathEndMode.ClosestTouch, Danger.Deadly))
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

    public static class Extend_Transition
    {
        public static Transition ReplaceState(this Transition self, LordToil old_state, LordToil new_state)
        {
            if (self.target == old_state) self.target = new_state;
            int index = self.sources.IndexOf(old_state);
            if (index != -1) self.sources[index] = new_state;
            return self;
        }
    }

    public static class Extend_StateGraph
    {
        public static StateGraph ReplaceState(this StateGraph self, LordToil old_state, LordToil new_state)
        {
            int index = self.lordToils.IndexOf(old_state);
            while(index != -1)
            {
                self.lordToils[index] = new_state;
                index = self.lordToils.IndexOf(old_state);
            }
            for(int i = 0; i < self.transitions.Count; ++i)
            {
                self.transitions[i] = self.transitions[i].ReplaceState(old_state, new_state);
            }
            return self;
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

            // All we need to do is replace the GatherAnimals and GatherSlaves LordToils with our versions.

            // OLD VERSIONS
            LordToil oldGatherAnimals = tthis.Field("gatherAnimals").GetValue<LordToil>();
            LordToil oldGatherSlaves = tthis.Field("gatherSlaves").GetValue<LordToil>();

            // OUR NEW VERSIONS
            LordToil_BillyCaravan_GatherAnimals billyGatherAnimals = new LordToil_BillyCaravan_GatherAnimals(meetingPoint);
            LordToil_BillyCaravan_GatherSlaves billyGatherSlaves = new LordToil_BillyCaravan_GatherSlaves(meetingPoint);

            // REPLACE member variables with new versions
            tthis.Field("gatherAnimals").SetValue(billyGatherAnimals);
            tthis.Field("gatherSlaves").SetValue(billyGatherSlaves);

            // REPLACE states in state machine with new versions
            __result = __result.ReplaceState(oldGatherAnimals, billyGatherAnimals);
            __result = __result.ReplaceState(oldGatherSlaves, billyGatherSlaves);
        }
    }

}
