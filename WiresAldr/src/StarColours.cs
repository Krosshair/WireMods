using System.Collections.Generic;
using UnityEngine;

namespace WiresAldr
{
    // The colours of stars past Valheim's looks: hand-picked per creature, 3 to 10 stars.
    // The creature shader adds these straight onto the texture's hue, saturation and
    // brightness (in linear light, without limits), so each number is worked out from that
    // creature's texture: the hue turns its main colour to the named one, the saturation
    // never goes below grey, and the brightness stays between half and 1.8 times its normal
    // one. That's why no colour can turn a creature black or white.
    internal static class StarColours
    {
        private static readonly Dictionary<string, Vector3[]> Looks = new Dictionary<string, Vector3[]>();

        // hue, saturation, brightness for a creature with this many stars (3 and up;
        // past 10 they start over). False for a creature that isn't listed.
        internal static bool TryGet(string prefab, int stars, out Vector3 look)
        {
            look = Vector3.zero;
            if (stars < 3 || !Looks.TryGetValue(prefab, out Vector3[] row)) return false;
            look = row[(stars - 3) % row.Length];
            return true;
        }

        private static Vector3 C(float hue, float saturation, float brightness) => new Vector3(hue, saturation, brightness);

        private static void Add(string prefabs, params Vector3[] looks)
        {
            foreach (string prefab in prefabs.Split(' ')) Looks[prefab] = looks;
        }

        static StarColours()
        {
            // bright rust, pale amber, night blue, deep green, plain azure, deep violet, bright red, bright yellow
            Add("Boar Boar_spiritcaller",
                C(-.017f, .1f, .062f), C(.05f, -.3f, .11f), C(-.442f, -.25f, -.069f), C(.253f, .15f, -.034f),
                C(-.489f, -.05f, .014f), C(-.303f, .15f, -.034f), C(-.061f, .1f, .062f), C(.078f, .1f, .062f));
            // pale amber, plain rose, pale emerald, deep azure, plain violet, pale cyan, deep rose, bright yellow
            Add("Deer_White",
                C(.042f, .15f, .07f), C(-.131f, .4f, .04f), C(.342f, .15f, .07f), C(-.497f, .6f, -.1f),
                C(-.311f, .4f, .04f), C(.453f, .15f, .07f), C(-.131f, .6f, -.1f), C(.069f, .55f, .07f));
            // bright orange, pale rose, deep emerald, plain azure, night violet, pale cyan, deep red, bright yellow
            Add("Deer",
                C(.025f, .03f, .045f), C(-.114f, -.2f, .045f), C(.358f, .08f, -.034f), C(-.481f, -.12f, .014f),
                C(-.294f, -.2f, -.069f), C(.469f, -.2f, .045f), C(-.053f, .08f, -.034f), C(.086f, .03f, .045f));
            // bright lime, plain teal, deep blue, bright orange, pale violet, night red, bright magenta, bright yellow
            Add("Neck",
                C(-.053f, .18f, .092f), C(.217f, .03f, .021f), C(.356f, .23f, -.051f), C(-.186f, .18f, .092f),
                C(.494f, -.22f, .164f), C(-.264f, -.17f, -.102f), C(-.394f, .18f, .092f), C(-.125f, .18f, .092f));
            // deep green, bright amber, pale azure, night rust, plain violet, bright red, pale teal, deep magenta
            Add("Greyling",
                C(.231f, .06f, -.023f), C(.028f, .01f, .041f), C(.489f, -.39f, .073f), C(-.039f, -.34f, -.04f),
                C(-.325f, -.14f, .009f), C(-.083f, .01f, .041f), C(.397f, -.39f, .073f), C(-.214f, .06f, -.023f));
            // bright lime, deep rust, pale blue, plain magenta, night emerald, bright orange, deep violet, bright red
            Add("Greydwarf",
                C(.128f, .01f, .041f), C(-.039f, .06f, -.023f), C(-.464f, -.39f, .073f), C(-.214f, -.14f, .009f),
                C(.328f, -.34f, -.04f), C(-.006f, .01f, .041f), C(-.325f, .06f, -.023f), C(-.083f, .01f, .041f));
            // deep emerald, bright rust, night azure, pale amber, deep magenta, plain teal, bright red, night violet
            Add("Greydwarf_Elite",
                C(.328f, .06f, -.023f), C(-.039f, .01f, .041f), C(.489f, -.34f, -.04f), C(.028f, -.39f, .073f),
                C(-.214f, .06f, -.023f), C(.397f, -.14f, .009f), C(-.083f, .01f, .041f), C(-.325f, -.34f, -.04f));
            // bright cyan, deep orange, plain magenta, pale lime, night blue, bright red, deep violet, bright yellow
            Add("Greydwarf_Shaman",
                C(.436f, .11f, .1f), C(-.008f, .16f, -.056f), C(-.217f, -.04f, .022f), C(.125f, -.29f, .178f),
                C(-.467f, -.24f, -.111f), C(-.086f, .11f, .1f), C(-.328f, .16f, -.056f), C(.053f, .11f, .1f));
            // bright azure, pale violet, deep teal, plain rose, night blue, bright emerald, pale amber, deep red
            Add("Greydwarf_Frozen Greydwarf_Shaman_Frozen Frysling",
                C(.022f, .5f, .129f), C(.208f, .1f, .23f), C(-.069f, .55f, -.072f), C(.389f, .35f, .029f),
                C(.069f, .15f, -.082f), C(-.139f, .5f, .129f), C(-.439f, .1f, .23f), C(.45f, .55f, -.072f));
            // deep green, plain violet, bright teal, pale amber, night red, bright orange, deep magenta, bright yellow
            Add("Troll Troll_sleeping",
                C(-.253f, .22f, -.058f), C(.192f, .02f, .023f), C(-.086f, .17f, .104f), C(-.456f, -.23f, .185f),
                C(.433f, -.18f, -.112f), C(-.489f, .17f, .104f), C(.303f, .22f, -.058f), C(-.428f, .17f, .104f));
            // bright cyan, deep blue, pale violet, plain emerald, night indigo, bright rose, deep teal, pale amber
            Add("TrollFrost",
                C(.128f, .46f, .089f), C(.225f, .46f, -.049f), C(.364f, .21f, .158f), C(.017f, .46f, .02f),
                C(.294f, .26f, -.092f), C(-.456f, .46f, .089f), C(.086f, .46f, -.049f), C(-.283f, .21f, .158f));
            // pale lime, deep rust, plain azure, night violet, bright red, pale cyan, deep green, bright amber
            Add("Skeleton Skeleton_Hildir Skeleton_Hildir_nochest Skeleton_Meadows Skeleton_Meadows_noarcher Skeleton_Mountains Skeleton_Mountains_noarcher Skeleton_NoArcher Skeleton_Poison Skeleton_Swamps Skeleton_Swamps_noarcher Skeleton_Friendly",
                C(.158f, -.41f, .082f), C(-.008f, .04f, -.025f), C(-.481f, -.16f, .01f), C(-.294f, -.36f, -.051f),
                C(-.053f, -.01f, .046f), C(.469f, -.41f, .082f), C(.261f, .04f, -.025f), C(.058f, -.01f, .046f));
            // pale violet, deep azure, plain emerald, night rose, bright blue, pale amber, deep teal, bright red
            Add("Skeleton_DeepNorth",
                C(.225f, -.36f, .07f), C(.039f, .09f, -.022f), C(-.122f, -.11f, .009f), C(.406f, -.31f, -.043f),
                C(.086f, .04f, .039f), C(-.422f, -.36f, .07f), C(-.053f, .09f, -.022f), C(.467f, .04f, .039f));
            // bright lime, deep teal, plain rust, pale azure, night magenta, bright red, deep violet, bright amber
            Add("Draugr Draugr_sleeping Draugr_Elite Draugr_Elite_sleeping",
                C(.033f, .18f, .044f), C(.303f, .19f, -.024f), C(-.133f, .03f, .01f), C(.394f, -.22f, .078f),
                C(-.308f, -.17f, -.049f), C(-.178f, .18f, .044f), C(-.419f, .19f, -.024f), C(-.067f, .18f, .044f));
            // bright emerald, deep rust, plain azure, pale amber, night violet, bright orange, deep magenta, bright red
            Add("Draugr_Ranged Draugr_Ranged_sleeping",
                C(.242f, .14f, .042f), C(-.125f, .19f, -.023f), C(.403f, -.01f, .009f), C(-.058f, -.26f, .074f),
                C(-.411f, -.21f, -.046f), C(-.092f, .14f, .042f), C(-.3f, .19f, -.023f), C(-.169f, .14f, .042f));
            // bright orange, deep magenta, plain green, night blue, pale amber, bright violet, deep teal, bright lime
            Add("Leech Leech_cave",
                C(.072f, -.18f, .118f), C(-.136f, -.13f, -.065f), C(.308f, -.33f, .026f), C(-.386f, -.53f, -.131f),
                C(.106f, -.58f, .21f), C(-.247f, -.18f, .118f), C(.475f, -.13f, -.065f), C(.206f, -.18f, .118f));
            // pale amber, deep azure, night rust, plain red, pale teal, deep violet, bright orange, bright yellow
            Add("Wolf Wolf_spiritcaller",
                C(.117f, .35f, .185f), C(-.422f, .4f, -.058f), C(.05f, .4f, -.115f), C(.006f, .4f, .023f),
                C(.486f, .35f, .185f), C(-.236f, .4f, -.058f), C(.083f, .4f, .104f), C(.144f, .4f, .104f));
            // bright rust, pale amber, deep blue, plain teal, night red, bright violet, deep emerald, bright yellow
            Add("Bjorn Bjorn_sleeping Bjorn_spiritcaller",
                C(.006f, .28f, .032f), C(.072f, -.12f, .058f), C(-.419f, .33f, -.018f), C(.442f, .13f, .007f),
                C(-.039f, -.07f, -.036f), C(-.281f, .28f, .032f), C(.372f, .33f, -.018f), C(.1f, .28f, .032f));
            // deep lime, plain azure, night magenta, bright orange, pale cyan, deep violet, bright red, pale yellow
            Add("Unbjorn",
                C(.172f, .16f, -.016f), C(-.467f, -.04f, .006f), C(-.169f, -.24f, -.031f), C(.039f, .11f, .028f),
                C(.483f, -.29f, .05f), C(-.281f, .16f, -.016f), C(-.039f, .11f, .028f), C(.1f, -.29f, .05f));
            // bright green, deep teal, plain orange, pale azure, night blue, bright magenta, deep violet, bright red
            Add("Goblin GoblinArcher Goblin_Gem",
                C(.156f, .01f, .114f), C(.322f, .06f, -.063f), C(-.081f, -.14f, .025f), C(.414f, -.39f, .203f),
                C(.461f, -.34f, -.127f), C(-.289f, .01f, .114f), C(-.4f, .06f, -.063f), C(-.158f, .01f, .114f));
            // bright teal, deep blue, plain violet, pale rose, night emerald, bright azure, deep magenta, bright amber
            Add("GoblinDeepNorth",
                C(.311f, .61f, .069f), C(.45f, .66f, -.038f), C(-.411f, .46f, .015f), C(-.231f, .21f, .122f),
                C(.242f, .26f, -.076f), C(.403f, .61f, .069f), C(-.3f, .66f, -.038f), C(-.058f, .61f, .069f));
            // deep orange, bright teal, plain red, night emerald, pale violet, deep blue, bright magenta, bright amber
            Add("GoblinBrute GoblinBrute_Hildir",
                C(-.156f, .02f, -.034f), C(.247f, -.03f, .061f), C(-.233f, -.18f, .014f), C(.178f, -.38f, -.068f),
                C(-.475f, -.43f, .109f), C(.386f, .02f, -.034f), C(-.364f, -.03f, .061f), C(-.122f, -.03f, .061f));
            // bright amber, pale azure, deep rust, plain emerald, pale violet, night blue, bright rose, bright yellow
            Add("Hare",
                C(.003f, .45f, .031f), C(.464f, .05f, .054f), C(-.064f, .46f, -.017f), C(.303f, .3f, .007f),
                C(-.35f, .05f, .054f), C(-.489f, .1f, -.028f), C(-.169f, .45f, .031f), C(.031f, .45f, .031f));
            // bright emerald, deep red, plain violet, pale azure, night amber, bright magenta, deep teal, bright yellow
            Add("Seeker",
                C(.35f, -.09f, .028f), C(-.061f, -.04f, -.016f), C(-.303f, -.24f, .006f), C(-.489f, -.49f, .05f),
                C(.05f, -.44f, -.028f), C(-.192f, -.09f, .028f), C(.419f, -.04f, -.016f), C(.078f, -.09f, .028f));
            // deep blue, bright orange, plain teal, night violet, pale lime, bright red, deep magenta, bright amber
            Add("SeekerBrute",
                C(-.383f, .18f, -.024f), C(.075f, .13f, .043f), C(.478f, -.02f, .01f), C(-.244f, -.22f, -.047f),
                C(.208f, -.27f, .076f), C(-.003f, .13f, .043f), C(-.133f, .18f, -.024f), C(.108f, .13f, .043f));
            // bright teal, deep violet, plain green, pale rose, night blue, bright orange, deep red, bright lime
            Add("Gjall",
                C(.458f, .11f, .089f), C(-.264f, .11f, -.049f), C(.292f, .11f, .02f), C(-.083f, .08f, .158f),
                C(-.403f, .11f, -.057f), C(.056f, .11f, .089f), C(-.022f, .11f, -.049f), C(.189f, .11f, .089f));
            // deep red, plain lime, pale azure, night violet, bright amber, deep teal, bright magenta, bright yellow
            Add("Tick",
                C(-.056f, .21f, -.076f), C(.156f, .01f, .038f), C(-.483f, -.24f, .301f), C(-.297f, -.19f, -.076f),
                C(.056f, .16f, .169f), C(.425f, .21f, -.076f), C(-.186f, .16f, .169f), C(.083f, .16f, .169f));
            // bright violet, deep red, plain amber, night blue, pale rose, bright emerald, deep magenta, bright yellow
            Add("Dverger DvergerAshlands DvergerDeepNorth DvergerMage DvergerMageFire DvergerMageIce DvergerMageSupport",
                C(.297f, -.03f, .062f), C(-.461f, .02f, -.034f), C(-.35f, -.18f, .014f), C(.158f, -.36f, -.069f),
                C(.478f, -.36f, .11f), C(-.05f, -.03f, .062f), C(.408f, .02f, -.034f), C(-.322f, -.03f, .062f));
            // bright orange, deep blue, plain violet, pale amber, night red, bright teal, deep magenta, bright yellow
            Add("Asksvin",
                C(-.219f, .28f, .045f), C(.322f, .33f, -.025f), C(.461f, .13f, .01f), C(-.186f, -.12f, .08f),
                C(-.297f, -.07f, -.032f), C(.183f, .28f, .045f), C(-.428f, .33f, -.025f), C(-.158f, .28f, .045f));
            // bright orange, deep blue, plain violet, pale amber, night red, bright teal, deep magenta, bright yellow
            Add("Asksvin_hatchling",
                C(-.178f, .16f, .059f), C(.364f, .18f, -.032f), C(-.497f, .01f, .013f), C(-.144f, -.24f, .104f),
                C(-.256f, -.19f, -.065f), C(.225f, .16f, .059f), C(-.386f, .18f, -.032f), C(-.117f, .16f, .059f));
            // bright orange, deep red, plain teal, bright violet, pale amber, night blue, bright emerald, bright rose
            Add("Charred_Archer Charred_Mage Charred_Melee Charred_Twitcher Charred_Archer_Fader Charred_Melee_Dyrnwyn Charred_Melee_Fader Charred_Twitcher_Summoned Troll_Summoned",
                C(-.461f, .23f, .017f), C(.461f, .28f, -.007f), C(-.058f, .08f, .004f), C(.219f, .23f, .017f),
                C(-.428f, -.17f, .03f), C(.081f, -.12f, -.007f), C(-.128f, .23f, .017f), C(.4f, .23f, .017f));
            // pale violet, deep rust, plain teal, night blue, bright amber, deep emerald, pale rose, bright red
            Add("Elaking ElakingLantern",
                C(-.311f, -.07f, .237f), C(-.025f, .38f, -.074f), C(.411f, .18f, .03f), C(-.45f, -.02f, -.112f),
                C(.042f, .33f, .133f), C(.342f, .38f, -.074f), C(-.131f, -.07f, .237f), C(-.069f, .33f, .133f));
            // pale azure, deep violet, plain emerald, night red, pale amber, deep blue, bright orange, pale cyan
            Add("FallenWarrior",
                C(-.464f, -.42f, .326f), C(-.278f, .03f, -.102f), C(.375f, -.17f, .041f), C(-.036f, -.37f, -.203f),
                C(.075f, -.42f, .326f), C(-.417f, .03f, -.102f), C(.042f, -.02f, .183f), C(.486f, -.42f, .326f));
            // bright rust, pale teal, deep violet, plain amber, night green, bright red, deep azure, pale yellow
            Add("Moose Moose_spiritcaller",
                C(-.017f, .39f, .089f), C(.419f, -.01f, .158f), C(-.303f, .4f, -.049f), C(.05f, .24f, .02f),
                C(.253f, .04f, -.099f), C(-.061f, .39f, .089f), C(-.489f, .4f, -.049f), C(.078f, -.01f, .158f));
            // pale azure, deep teal, plain amber, night blue, pale rose, deep violet, bright orange, bright yellow
            Add("Seal",
                C(-.464f, .32f, .322f), C(.444f, .4f, -.1f), C(.075f, .4f, .04f), C(-.417f, .37f, -.201f),
                C(-.097f, .32f, .322f), C(-.278f, .4f, -.1f), C(.042f, .4f, .181f), C(.103f, .4f, .181f));
        }
    }
}
