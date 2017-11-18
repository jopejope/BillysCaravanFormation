using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Harmony;
using System.Reflection;

namespace BillysCaravanFormation
{
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
