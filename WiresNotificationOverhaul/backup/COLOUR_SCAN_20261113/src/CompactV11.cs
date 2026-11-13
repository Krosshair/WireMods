using System;
using System.Text.RegularExpressions;

namespace WiresNotificationOverhaul
{
    // All rules apply ONLY when a notification event enters MessageHud.
    // No periodic scans, Update handlers, extra canvases or mod dependencies.
    // Only full-message matches are rewritten; unknown text remains intact.
    internal static class CompactV11
    {
        private sealed class Rule
        {
            internal readonly Regex Pattern;
            internal readonly string Replacement;
            internal readonly char Initial;

            internal Rule(string pattern, string replacement)
            {
                // Cache the pattern, but don't JIT-compile dozens of
                // uncommon message patterns at game startup.
                Pattern = new Regex(pattern,
                    RegexOptions.CultureInvariant |
                    RegexOptions.IgnoreCase);
                Replacement = replacement;
                Initial = pattern.Length > 1 && pattern[0] == '^' &&
                    char.IsLetter(pattern[1])
                    ? char.ToUpperInvariant(pattern[1]) : '\0';
            }
        }

        // Rules use known Valheim phrases and verified Quick Stack / Restock
        // language strings. The generalized rules work with mods that route
        // notices through MessageHud (EpicLoot, crafting, raids, trading etc.)
        // WHEN their message actually matches, without guessing meaning.
        private static readonly Rule[] Rules =
        {
            // Station requirements are true environmental requirements,
            // not inventory or equipment errors.
            new Rule(@"^(?:This |The )?crafting station needs a roof[.!]?$",
                "Requires roof"),
            new Rule(@"^(?:This |The )?crafting station (?:is )?too exposed[.!]?$",
                "Requires shelter"),
            new Rule(@"^(?:This |The )?station (?:is )?too exposed[.!]?$",
                "Requires shelter"),
            new Rule(@"^(?:This |The )?(?:crafting )?station requires (?:a )?roof[.!]?$",
                "Requires roof"),
            new Rule(@"^(?:You need (?:a )?roof|Roof required|Requires a roof)[.!]?$",
                "Requires roof"),
            new Rule(@"^(?:You need to be sheltered|Shelter required|Not enough shelter)[.!]?$",
                "Requires shelter"),
            new Rule(@"^(?:You need a fire nearby|A nearby fire is required)[.!]?$",
                "Requires nearby fire"),
            new Rule(@"^(?:You need a crafting station|Crafting station required)[.!]?$",
                "Requires station"),
            // Quick Stack / Store / Sort / Trash / Restock, verified against
            // QuickStackStore.English.json from the player's installed mod.
            new Rule(@"^Nothing to (?:quick )?stack[.!]?$",
                "Nothing to stack"),
            new Rule(@"^Stacked 0 items?[.!]?$",
                "Nothing stacked"),
            new Rule(@"^Stacked (\d+) items?[.!]?$",
                "Stacked $1"),
            new Rule(@"^Nothing to restock[.!]?$",
                "Nothing to restock"),
            new Rule(@"^Couldn.t restock\s*\((\d+)\s*/\s*(\d+)\)[.!]?$",
                "Restock failed ($1/$2)"),
            new Rule(@"^Partially restocked\s*\((\d+)\s*/\s*(\d+)\)[.!]?$",
                "Restocked ($1/$2)"),
            new Rule(@"^Fully restocked\s*\(total:\s*(\d+)\)[.!]?$",
                "Restocked ($1)"),
            new Rule(@"^Can.t trash favorited item[.!]?$",
                "Favorited item protected"),
            new Rule(@"^Can.t trash flag a favorited item[.!]?$",
                "Favorited item: can't trash-flag"),
            new Rule(@"^Settings disallow trashing of item in hotkey bar[.!]?$",
                "Hotbar item protected"),
            new Rule(@"^Can.t favorite a trash flagged item[.!]?$",
                "Trash-flagged item: can't favorite"),

            // Valheim inventory / resource / equip error messages.
            new Rule(@"^(?:You (?:have |do )?not have|You need) enough (stamina|eitr|health)[.!]?$",
                "Not enough $1"),
            new Rule(@"^You (?:don.t|do not) have enough (stamina|eitr|health)[.!]?$",
                "Not enough $1"),
            new Rule(@"^You are (?:now )?(?:over[- ]encumbered|encumbered|overburdened|overloaded)[.!]?$",
                "Overburdened"),
            new Rule(@"^You (?:can.t|cannot) carry any more[.!]?$",
                "Overburdened"),
            new Rule(@"^You need more free inventory space[.!]?$",
                "Need inventory space"),
            new Rule(@"^(?:There is |You have )no (?:more )?(?:room|space) in your inventory[.!]?$",
                "Inventory full"),
            new Rule(@"^You (?:are )?(?:too far away|out of range)(?: to interact)?[.!]?$",
                "Too far away"),
            new Rule(@"^You (?:are |were )?unable to equip (.+)[.!]?$",
                "Can't equip $1"),
            new Rule(@"^You need (?:to have )?a (.+) nearby[.!]?$",
                "Need nearby $1"),
            new Rule(@"^You need (?:a |an )?(.+) to (?:craft|build|repair) (.+)[.!]?$",
                "Need $1 for $2"),

            // Saves, survival, travel and world notifications.
            new Rule(@"^(?:World (?:is )?saving|Saving (?:the )?world)(?:\.{0,3})$",
                "Saving world"),
            new Rule(@"^(?:World (?:has been )?saved|Saved (?:the )?world|World save completed|Save completed)\s*(?:in\s*|\(\s*)?(\d+(?:[.,]\d+)?)\s*(ms|milliseconds?|s|sec|seconds?)\)?[.!]?$",
                "World saved ($1$2)"),
            new Rule(@"^(?:World (?:has been )?saved|World save completed|Save completed)[.!]?$",
                "World saved"),
            new Rule(@"^(?:Your )?spawn point (?:is now )?(?:set|updated)[.!]?$",
                "Spawn point set"),
            new Rule(@"^You (?:have )?(?:recovered|retrieved) (?:your )?items[.!]?$",
                "Items recovered"),
            new Rule(@"^Your (items|equipment) (?:have been |were )?recovered[.!]?$",
                "$1 recovered"),
            new Rule(@"^You (?:are )?(?:now )?being hunted[.!]?$",
                "Hunted"),
            new Rule(@"^You are (?:no longer|not) sheltered[.!]?$",
                "Exposed"),
            new Rule(@"^You (?:have )?(?:connected|linked) (?:the )?portal[.!]?$",
                "Portal connected"),
            new Rule(@"^(?:Portal|The portal) (?:is )?(?:not connected|unconnected)[.!]?$",
                "Portal unconnected"),
            new Rule(@"^(?:Your )?portal destination (?:is )?missing[.!]?$",
                "No portal destination"),

            // Skills, crafting, stations, companions and quest systems.
            new Rule(@"^You (?:have )?(?:successfully )?(?:crafted|created) (.+)[.!]?$",
                "Crafted $1"),
            new Rule(@"^You (?:have )?(?:successfully )?upgraded (.+)[.!]?$",
                "Upgraded $1"),
            new Rule(@"^You (?:have )?(?:successfully )?repaired (.+)[.!]?$",
                "Repaired $1"),
            new Rule(@"^You (?:have )?learned (?:how to make |the recipe for )(.+)[.!]?$",
                "Recipe: $1"),
            new Rule(@"^New recipe (?:unlocked|discovered):?\s*(.+)[.!]?$",
                "Recipe: $1"),
            new Rule(@"^New (?:quest|mission):?\s*(.+)[.!]?$",
                "Quest: $1"),
            new Rule(@"^(?:Quest|Bounty) (?:has been )?completed:?\s*(.+)[.!]?$",
                "Completed: $1"),
            new Rule(@"^(?:You have )?completed (?:the )?(quest|bounty):?\s*(.+)[.!]?$",
                "$1 complete: $2"),
            new Rule(@"^You (?:have )?(?:successfully )?enchanted (.+)[.!]?$",
                "Enchanted $1"),
            new Rule(@"^You (?:have )?(?:successfully )?augmented (.+)[.!]?$",
                "Augmented $1"),
            new Rule(@"^You (?:have )?(?:successfully )?disenchanted (.+)[.!]?$",
                "Disenchanted $1"),
            new Rule(@"^You (?:have )?(?:successfully )?sold (.+)[.!]?$",
                "Sold $1"),
            new Rule(@"^You (?:have )?(?:successfully )?purchased (.+)[.!]?$",
                "Bought $1"),
            new Rule(@"^You (?:have )?(?:successfully )?bought (.+)[.!]?$",
                "Bought $1"),
            new Rule(@"^(?:Your )?(?:companion|pet) (.+) (?:has )?leveled up(?: to level (\d+))?[.!]?$",
                "$1 level $2"),

            // Fishing and gathering notices (preserve fish/bait/item names).
            new Rule(@"^You (?:have )?caught (?:a |an )?(.+)[.!]?$",
                "Caught $1"),
            new Rule(@"^You (?:have )?hooked (?:a |an )?(.+)[.!]?$",
                "Hooked $1"),
            new Rule(@"^(?:Your )?fishing line (?:has )?(?:snapped|broken)[.!]?$",
                "Line snapped"),
            new Rule(@"^You (?:have )?run out of (.+ bait)[.!]?$",
                "Out of $1"),
            new Rule(@"^You (?:have )?(?:collected|harvested|gathered) (.+)[.!]?$",
                "Gathered $1")
        };

        internal static bool TryFormat(string original, out string compact)
        {
            compact = null;
            if (string.IsNullOrWhiteSpace(original))
                return false;
            string input = original.Trim();

            // Don't damage nested formatting tags, chat-like prose,
            // colorized loot names or long multi-line instructions.
            if (input.Length > 512 || input.IndexOf('<') >= 0 ||
                input.IndexOf('\n') >= 0 || input.IndexOf('\r') >= 0)
                return false;

            char initial = char.ToUpperInvariant(input[0]);
            for (int i = 0; i < Rules.Length; ++i)
            {
                Rule rule = Rules[i];
                if (rule.Initial != '\0' && rule.Initial != initial)
                    continue;
                Match match = rule.Pattern.Match(input);
                if (!match.Success)
                    continue;
                compact = match.Result(Rules[i].Replacement);
                if (compact.EndsWith("level ", StringComparison.Ordinal))
                    compact = compact.Substring(0, compact.Length - 6);
                return !string.Equals(compact, input, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
