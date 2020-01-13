using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Harmony;
using System.Reflection;
using UnityEngine;

namespace BillysCaravanFormation
{
    public class CaravanModSettings : ModSettings
    {
        public bool fastAnimalCollection = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref fastAnimalCollection, "fastCaravanAnimalCollection");
            base.ExposeData();
        }
    }
    public class CaravanModInit : Verse.Mod
    {
        CaravanModSettings settings;

        public CaravanModInit(ModContentPack content)
            : base(content)
        {
            this.settings = GetSettings<CaravanModSettings>();
            // this should be executed when Rimworld loads our mod (because the class inherits from Verse.Mod). 
            // The code below tells Harmony to apply the patches attributed above.
            var harmony = HarmonyInstance.Create("bem.rimworld.mod.billy.smartcaravan");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("Skip gathering of caravan animals by colonists", ref settings.fastAnimalCollection, "Animals will move to the meeting spot on their own, speeding up caravan formation");
            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Billy's Caravan Formation";
        }
    }
}
