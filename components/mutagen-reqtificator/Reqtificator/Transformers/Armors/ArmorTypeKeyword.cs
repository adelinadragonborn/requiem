﻿using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Reqtificator.StaticReferences;
using ArmorTransformationResult =
    Reqtificator.Transformers.TransformationResult<Mutagen.Bethesda.Skyrim.Armor, Mutagen.Bethesda.Skyrim.IArmorGetter>;

namespace Reqtificator.Transformers.Armors
{
    internal class ArmorTypeKeyword : TransformerV2<Armor, IArmorGetter>
    {
        public override ArmorTransformationResult Process(ArmorTransformationResult input)
        {
            return (input.Record().TemplateArmor.IsNull, input.Record().BodyTemplate?.ArmorType) switch
            {
                (true, ArmorType.HeavyArmor) => AddKeywordIfMissing(input, Keywords.ArmorHeavy.FormKey),
                (true, ArmorType.LightArmor) => AddKeywordIfMissing(input, Keywords.ArmorLight.FormKey),
                _ => input
            };
        }

        private static ArmorTransformationResult AddKeywordIfMissing(ArmorTransformationResult input, FormKey keyword)
        {
            if (input.Record().Keywords?.ContainsNot(keyword) ?? true)
            {
                return input.Modify(armor =>
                {
                    armor.Keywords ??= new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
                    armor.Keywords.Add(keyword);
                });
            }

            return input;
        }
    }
}