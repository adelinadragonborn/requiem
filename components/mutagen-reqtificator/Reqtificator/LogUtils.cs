﻿using System;
using System.IO;
using Mutagen.Bethesda;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace Reqtificator
{
    public static class LogUtils
    {
        private const string LogFileName = "Reqtificator.log";

        public static void SetUpLogging()
        {
            File.Delete(LogFileName);
            var logSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);
            const string format =
                "{Timestamp} [{Level:u3}] {RecordFormId} - {Message:lj} ({RecordEditorId}){NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(LogFileName, levelSwitch: logSwitch, outputTemplate: format)
                .WriteTo.Console(levelSwitch: logSwitch, outputTemplate: format)
                .CreateLogger();
        }

        public static Func<Func<T>, T> WithRecordContextLog<T>(IMajorRecordGetter record)
        {
            return f =>
            {
                using (LogContext.PushProperty("RecordEditorId", record.EditorID))
                {
                    using (LogContext.PushProperty("RecordFormId", record.FormKey.ToString()))
                    {
                        return f();
                    }
                }
            };
        }
    }
}