using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Mutagen.Bethesda.Cache.Implementations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Reqtificator.Configuration;
using Reqtificator.Events;
using Reqtificator.Export;
using Reqtificator.StaticReferences;
using Reqtificator.Transformers;
using Reqtificator.Transformers.Actors;
using Reqtificator.Transformers.Armors;
using Reqtificator.Transformers.EncounterZones;
using Reqtificator.Transformers.LeveledCharacters;
using Reqtificator.Transformers.LeveledItems;
using Reqtificator.Transformers.Rules;
using Reqtificator.Transformers.Weapons;
using Reqtificator.Utils;
using Serilog;

namespace Reqtificator
{
    internal class MainLogicExecutor
    {
        private readonly InternalEvents _events;
        private readonly GameContext _context;
        private readonly ReqtificatorConfig _reqtificatorConfig;


        public MainLogicExecutor(InternalEvents events, GameContext context, ReqtificatorConfig reqtificatorConfig)
        {
            _events = events;
            _context = context;
            _reqtificatorConfig = reqtificatorConfig;
        }

        public ErrorOr<SkyrimMod> GeneratePatch(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
            UserSettings userSettings, ModKey outputModKey)
        {
            var requiemModKey = new ModKey("Requiem", ModType.Plugin);
            var importedModsLinkCache = loadOrder.ToImmutableLinkCache();
            var reqTags = new ReqTagParser(_events).ParseTagsFromModHeaders(loadOrder);
            var modsWithCompactLeveledLists = reqTags
                .Where(kv => kv.Value.Contains(ReqTags.CompactLeveledLists))
                .Select(kv => kv.Key).ToImmutableHashSet().Add(requiemModKey);
            var modsWithTemperedItems = reqTags
                .Where(kv => kv.Value.Contains(ReqTags.TemperedItems))
                .Select(kv => kv.Key).ToImmutableHashSet().Add(requiemModKey);

            var numberOfRecords = loadOrder.PriorityOrder.Armor().WinningOverrides().Count() +
                                  loadOrder.PriorityOrder.Weapon().WinningOverrides().Count();
            _events.PublishPatchStarted(numberOfRecords);

            var ammoPatched = PatchAmmunition(loadOrder);
            var encounterZonesPatched = PatchEncounterZones(loadOrder, userSettings);
            var doorsPatched = PatchDoors(loadOrder);
            var containersPatched = PatchContainers(loadOrder);
            var leveledItemsPatched = PatchLeveledItems(loadOrder, modsWithCompactLeveledLists, modsWithTemperedItems);
            var leveledCharactersPatched = PatchLeveledCharacters(loadOrder, modsWithCompactLeveledLists);
            var armorsPatched = PatchArmors(loadOrder);
            var weaponsPatched = PatchWeapons(loadOrder);
            var actorsPatched = PatchActors(loadOrder, importedModsLinkCache);

            Log.Information("adding patched records to output mod");

            var outputMod = new SkyrimMod(outputModKey, SkyrimRelease.SkyrimSE).AsSuccess()
                .Map(m => m.WithRecords(encounterZonesPatched))
                .Map(m => m.WithRecords(doorsPatched))
                .Map(m => m.WithRecords(containersPatched))
                .Map(m => m.WithRecords(leveledItemsPatched))
                .Map(m => m.WithRecords(leveledCharactersPatched))
                .FlatMap(m => armorsPatched.Map(m.WithRecords))
                .Map(m => m.WithRecords(ammoPatched))
                .FlatMap(m => weaponsPatched.Map(m.WithRecords))
                .FlatMap(m => actorsPatched.Map(m.WithRecords));

            var requiem = loadOrder.PriorityOrder.First(x => x.ModKey == requiemModKey);

            var version = new RequiemVersion(5, 0, 0, "a Phoenix perhaps?");
            _ = outputMod.Map(m =>
            {
                PatchData.SetPatchHeadersAndVersion(requiem.Mod!, m, version);
                return m;
            });

            return outputMod;
        }

        private static ImmutableList<Ammunition> PatchAmmunition(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder)
        {
            var ammoRecords = loadOrder.PriorityOrder.Ammunition().WinningOverrides();
            return new AmmunitionTransformer().ProcessCollection(ammoRecords);
        }

        private static ImmutableList<EncounterZone> PatchEncounterZones(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
            UserSettings userSettings)
        {
            var encounterZones = loadOrder.PriorityOrder.EncounterZone().WinningOverrides();
            return new OpenCombatBoundaries(loadOrder, userSettings).ProcessCollection(encounterZones);
        }

        private static ImmutableList<Door> PatchDoors(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder)
        {
            var doors = loadOrder.PriorityOrder.Door().WinningOverrides();
            return new CustomLockpicking<Door, IDoor, IDoorGetter>().ProcessCollection(doors);
        }

        private static ImmutableList<Container> PatchContainers(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder)
        {
            var containers = loadOrder.PriorityOrder.Container().WinningOverrides();
            return new CustomLockpicking<Container, IContainer, IContainerGetter>().ProcessCollection(containers);
        }

        private static ImmutableList<LeveledItem> PatchLeveledItems(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
            IImmutableSet<ModKey> modsWithCompactLeveledLists, IImmutableSet<ModKey> modsWithTemperedItems)
        {
            var leveledItems = loadOrder.PriorityOrder.LeveledItem().WinningOverrides();
            return new CompactLeveledItemUnrolling(modsWithCompactLeveledLists)
                .AndThen(new TemperedItemGeneration(modsWithTemperedItems))
                .ProcessCollection(leveledItems);
        }

        private static ImmutableList<LeveledNpc> PatchLeveledCharacters(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
            IImmutableSet<ModKey> modsWithCompactLeveledLists)
        {
            var leveledCharacters = loadOrder.PriorityOrder.LeveledNpc().WinningOverrides();
            return new CompactLeveledCharacterUnrolling(modsWithCompactLeveledLists).ProcessCollection(
                leveledCharacters);
        }

        private ErrorOr<ImmutableList<Armor>> PatchArmors(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder)
        {
            var armors = loadOrder.PriorityOrder.Armor().WinningOverrides();
            var armorRules = RecordUtils.LoadModConfigFiles(_context, "ArmorKeywordAssignments")
                .FlatMap(configs => configs.Select(x =>
                        AssignmentsFromRules.LoadKeywordRules<IArmorGetter>(x.Item2, x.Item1))
                    .Aggregate(ImmutableList<AssignmentRule<IArmorGetter, IKeywordGetter>>.Empty.AsSuccess(),
                        (acc, elem) => acc.FlatMap(list => elem.Map(list.AddRange)))
                );

            return armorRules.Map(rules =>
                new ArmorTypeKeyword()
                    .AndThen(new ArmorRatingScaling(_reqtificatorConfig.ArmorSettings))
                    .AndThen(new ArmorKeywordsFromRules(rules))
                    .AndThen(new ProgressReporter<Armor, IArmorGetter>(_events))
                    .ProcessCollection(armors)
            );
        }

        private ErrorOr<ImmutableList<Weapon>> PatchWeapons(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder)
        {
            var weapons = loadOrder.PriorityOrder.Weapon().WinningOverrides();
            var weaponRules = RecordUtils.LoadModConfigFiles(_context, "WeaponKeywordAssignments")
                .FlatMap(configs => configs.Select(x =>
                        AssignmentsFromRules.LoadKeywordRules<IWeaponGetter>(x.Item2, x.Item1))
                    .Aggregate(ImmutableList<AssignmentRule<IWeaponGetter, IKeywordGetter>>.Empty.AsSuccess(),
                        (acc, elem) => acc.FlatMap(list => elem.Map(list.AddRange)))
                );
            return weaponRules.Map(rules =>
                new WeaponDamageScaling().AndThen(new WeaponMeleeRangeScaling())
                    .AndThen(new WeaponNpcAmmunitionUsage())
                    .AndThen(new WeaponRangedSpeedScaling())
                    .AndThen(new WeaponKeywordsFromRules(rules))
                    .AndThen(new ProgressReporter<Weapon, IWeaponGetter>(_events))
                    .ProcessCollection(weapons));
        }

        private ErrorOr<ImmutableList<Npc>> PatchActors(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
            ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> importedModsLinkCache)
        {
            var actorPerkRules = RecordUtils.LoadModConfigFiles(_context, "ActorAssignmentRules")
                .FlatMap(configs => configs.Select(x =>
                        AssignmentsFromRules.LoadPerkRules(x.Item2, x.Item1, importedModsLinkCache))
                    .Aggregate(ImmutableList<AssignmentRule<INpcGetter, IPerkGetter>>.Empty.AsSuccess(),
                        (acc, elem) => acc.FlatMap(list => elem.Map(list.AddRange)))
                );
            var actorSpellRules = RecordUtils.LoadModConfigFiles(_context, "ActorAssignmentRules")
                .FlatMap(configs => configs.Select(x =>
                        AssignmentsFromRules.LoadSpellRules(x.Item2, x.Item1, importedModsLinkCache))
                    .Aggregate(ImmutableList<AssignmentRule<INpcGetter, ISpellGetter>>.Empty.AsSuccess(),
                        (acc, elem) => acc.FlatMap(list => elem.Map(list.AddRange)))
                );
            var actorRules = actorPerkRules.FlatMap(perks => actorSpellRules.Map(spells => (perks, spells)));

            var actors = loadOrder.PriorityOrder.Npc().WinningOverrides();
            var globalPerks =
                RecordUtils.GetRecordsFromAllImports<IPerkGetter>(FormLists.GlobalPerks, importedModsLinkCache);
            return globalPerks.FlatMap(perks => actorRules.Map(rules =>
                new ActorCommonScripts(importedModsLinkCache)
                    .AndThen(new ActorGlobalPerks(perks))
                    .AndThen(new ActorPerksFromRules(rules.perks))
                    .AndThen(new ActorSpellsFromRules(rules.spells))
                    .AndThen(new PlayerChanges(_reqtificatorConfig.PlayerConfig))
                    .ProcessCollection(actors)));
        }
    }
}