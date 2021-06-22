using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Reqtificator.Configuration;
using Reqtificator.Export;
using Reqtificator.Transformers;
using Reqtificator.Transformers.Armors;
using Reqtificator.Transformers.EncounterZones;

namespace Reqtificator
{
    internal static class MainLogic
    {
        public static SkyrimMod GeneratePatch(LoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
            UserSettings userConfig, ModKey outputModKey)
        {
            //TODO: read this from the actual config files and pass as a argument to the function
            var armorConfig = new ArmorPatchingConfiguration(
                HeavyArmorRatingThresholds: new ArmorRatingThresholds(
                    Body: 74,
                    Feet: 27,
                    Hands: 27,
                    Head: 35,
                    Shield: 54),
                LightArmorRatingThresholds: new ArmorRatingThresholds(
                    Body: 62,
                    Feet: 18,
                    Hands: 18,
                    Head: 26,
                    Shield: 44
                )
            );

            var requiemModKey = new ModKey("Requiem", ModType.Plugin);
            var outputMod = new SkyrimMod(outputModKey, SkyrimRelease.SkyrimSE);

            var ammoRecords = loadOrder.PriorityOrder.Ammunition().WinningOverrides();
            var ammoPatched = new AmmunitionTransformer().ProcessCollection(ammoRecords);

            var encounterZones = loadOrder.PriorityOrder.EncounterZone().WinningOverrides();
            var encounterZonesPatched =
                new OpenCombatBoundaries(loadOrder, userConfig).ProcessCollection(encounterZones);

            var doors = loadOrder.PriorityOrder.Door().WinningOverrides();
            var doorsPatched = new CustomLockpicking<Door, IDoor, IDoorGetter>().ProcessCollection(doors);

            var containers = loadOrder.PriorityOrder.Container().WinningOverrides();
            var containersPatched =
                new CustomLockpicking<Container, IContainer, IContainerGetter>().ProcessCollection(containers);

            var armors = loadOrder.PriorityOrder.Armor().WinningOverrides();
            var armorsPatched = new ArmorTypeKeyword().AndThen(new ArmorRatingScaling(armorConfig))
                .ProcessCollection(armors);

            encounterZonesPatched.ForEach(r => outputMod.EncounterZones.Add(r));
            doorsPatched.ForEach(r => outputMod.Doors.Add(r));
            containersPatched.ForEach(r => outputMod.Containers.Add(r));
            armorsPatched.ForEach(r => outputMod.Armors.Add(r));
            ammoPatched.ForEach(r => outputMod.Ammunitions.Add(r));

            var requiem = loadOrder.PriorityOrder.First(x => x.ModKey == requiemModKey);

            var version = new RequiemVersion(5, 0, 0, "a Phoenix perhaps?");
            PatchData.SetPatchHeadersAndVersion(requiem.Mod!, outputMod, version);

            return outputMod;
        }

        public static void WritePatchToDisk(SkyrimMod generatedPatch, string outputDirectory)
        {
            generatedPatch.WriteToBinaryParallel(Path.Combine(outputDirectory, generatedPatch.ModKey.FileName));
        }
    }
}