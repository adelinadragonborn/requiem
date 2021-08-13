﻿using System;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Reqtificator.Configuration;
using Reqtificator.Events;
using Serilog;
using Serilog.Events;

[assembly: CLSCompliant(false)]
[assembly: InternalsVisibleTo("ReqtificatorTest")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")] //for mocking internal entities
[assembly: NeutralResourcesLanguage("en-US")]


//TODO: figure out why I need to do this when adding Mutagen as a dependency
namespace System.Runtime.CompilerServices
{
    public record IsExternalInit;
}

namespace Reqtificator
{
    internal class Backend
    {
        private const GameRelease Release = GameRelease.SkyrimSE;
        private static readonly ModKey PatchModKey = ModKey.FromNameAndExtension("Requiem for the Mutated.esp");
        private static readonly ModKey RequiemModKey = new ModKey("Requiem", ModType.Plugin);

        private readonly InternalEvents _events;
        private readonly ReqtificatorLogContext _logs;

        public Backend(InternalEvents eventsQueue, ReqtificatorLogContext logContext)
        {
            _events = eventsQueue;
            _logs = logContext;
            WarmupSkyrim.Init();

            var context = GameContext.GetRequiemContext(Release, PatchModKey);
            var userConfig = LoadAndVerifyUserSettings(context);

            _events.PublishReadyToPatch(userConfig, context.ActiveMods.Select(x => x.ModKey));
            Log.Information("Ready to patch: userConfig detected\r\n{userConfig}", userConfig);

            _events.PatchRequested += (_, updatedSettings) =>
            {
                var logLevel = updatedSettings.VerboseLogging ? LogEventLevel.Debug : LogEventLevel.Information;
                _logs.LogLevel.MinimumLevel = logLevel;
                updatedSettings.WriteToFile(Path.Combine(context.DataFolder, "Reqtificator", "UserSettings.json"));
                var generatedPatch = GeneratePatch(context, updatedSettings, Release, PatchModKey);
                Log.Information("done patching, now exporting to disk");
                MainLogic.WritePatchToDisk(generatedPatch, context.DataFolder);
                Log.Information("done exporting");
                _events.PublishFinished(ReqtificatorOutcome.Success);
            };
        }

        public UserSettings LoadAndVerifyUserSettings(GameContext context)
        {
            var userConfigFile = Path.Combine(context.DataFolder, "Reqtificator", "UserSettings.json");
            var userConfig = UserSettings.LoadUserSettings(userConfigFile);

            //TODO: refactor this into a nice verification function & better error handling
            if (context.ActiveMods.All(x => x.ModKey != RequiemModKey))
            {
                Log.Error("oops, where's your Requiem.esp?");
                _events.PublishFinished(ReqtificatorOutcome.MissingRequiem);
            }

            return userConfig;
        }


        public SkyrimMod GeneratePatch(GameContext context, UserSettings userConfig, GameRelease release,
            ModKey patchModKey)
        {
            try
            {
                var loadOrder = LoadOrder.Import<ISkyrimModGetter>(context.DataFolder, context.ActiveMods, release);
                //TODO: update base folder for configurations if needed
                var configFolder = Path.Combine(context.DataFolder, "SkyProc Patchers", "Requiem", "Config");
                var reqtificatorConfig = ReqtificatorConfig.LoadFromConfigs(configFolder, loadOrder);

                Log.Information("start patching");

                return MainLogic.GeneratePatch(loadOrder, userConfig, _events, reqtificatorConfig, context,
                        patchModKey) switch
                {
                    Success<SkyrimMod> s => s.Value,
                    Failed<SkyrimMod> f => throw f.Error,
                    _ => throw new NotImplementedException()
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "things did not go according to plan");
                _events.PublishFinished(ReqtificatorFailure.CausedBy(ex));
                throw;
            }
        }
    }
}