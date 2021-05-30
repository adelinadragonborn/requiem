﻿using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;


namespace Reqtificator.StaticReferences
{
    public static class Keywords
    {
        public static readonly FormKey AlreadyReqtified = FormKey.Factory("8F08B5:Requiem.esp");
        public static readonly FormKey NoDamageRescaling = FormKey.Factory("AD3B2D:Requiem.esp");
        public static readonly FormKey NoWeaponReachRescaling = FormKey.Factory("AD3B2E:Requiem.esp");
    }

    public static class GlobalVariables
    {
        public static readonly FormKey VersionStamp = FormKey.Factory("973D69:Requiem.esp");
    }

    public static class FormLists
    {
        public static readonly FormLink<FormList> ClosedEncounterZones = new(FormKey.Factory("A46546:Requiem.esp"));
    }
}