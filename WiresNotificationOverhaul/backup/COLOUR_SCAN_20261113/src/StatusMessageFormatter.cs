using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiresNotificationOverhaul
{
    // Find the *actual* status-effect definition that emitted a
    // message, including equipment and modded effects. Called once
    // when an eligible notification enters the stack, not per frame.
    internal static class StatusMessageFormatter
    {
        internal static bool TryResolve(string raw, string localized,
            out string compactText, out Sprite icon)
        {
            compactText = null;
            icon = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            // Rested has a comfort number that must not be lost.
            if (raw.IndexOf("$se_rest", StringComparison.Ordinal) >= 0)
                return false;

            // Avoid expensive prefab queries for ordinary messages,
            // e.g. Out of Arrows, pickup notices and map messages.
            string sentence = localized ?? "";
            if (!raw.StartsWith("$se_", StringComparison.Ordinal) &&
                !raw.StartsWith("$msg_", StringComparison.Ordinal) &&
                !sentence.StartsWith("You ", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
                return false;

            Player player = Player.m_localPlayer;

            // Prefer equipped items, so "You feel stronger" from
            // Meginjord is never mislabeled as another piece of gear.
            if (player != null && player.GetInventory() != null)
            {
                List<ItemDrop.ItemData> equipped =
                    player.GetInventory().GetEquippedItems();
                if (equipped != null)
                {
                    foreach (ItemDrop.ItemData item in equipped)
                        if (item != null && item.m_shared != null &&
                            MatchEffect(item.m_shared.m_equipStatusEffect,
                                raw, localized,
                                Localize(item.m_shared.m_name),
                                item.GetIcon(), true,
                                out compactText, out icon))
                            return true;
                }
            }

            // Active player effect takes precedence over all generic
            // status prefabs, including statuses added by other mods.
            if (player != null)
            {
                SEMan manager = player.GetSEMan();
                if (manager != null)
                {
                    List<StatusEffect> active = manager.GetStatusEffects();
                    if (active != null)
                    {
                        foreach (StatusEffect effect in active)
                            if (MatchEffect(effect, raw, localized,
                                null, null, false,
                                out compactText, out icon))
                                return true;
                    }
                }
            }

            ObjectDB db = ObjectDB.instance;
            if (db == null)
                return false;

            if (db.m_StatusEffects != null)
            {
                foreach (StatusEffect effect in db.m_StatusEffects)
                    if (MatchEffect(effect, raw, localized,
                        null, null, false, out compactText, out icon))
                        return true;
            }

            if (db.m_items != null)
            {
                // An equipped item can already be removed when its
                // effect sends a stop message. Find that item by
                // its effect definition, rather than guessing based
                // on phrases or an arbitrary status name.
                foreach (GameObject prefab in db.m_items)
                {
                    if (prefab == null)
                        continue;
                    ItemDrop drop = prefab.GetComponent<ItemDrop>();
                    if (drop == null || drop.m_itemData == null ||
                        drop.m_itemData.m_shared == null)
                        continue;

                    ItemDrop.ItemData data = drop.m_itemData;
                    if (MatchEffect(data.m_shared.m_equipStatusEffect,
                        raw, localized, Localize(data.m_shared.m_name),
                        data.GetIcon(), true, out compactText, out icon))
                        return true;

                    if (MatchEffect(data.m_shared.m_consumeStatusEffect,
                        raw, localized, null, null, false,
                        out compactText, out icon))
                        return true;

                    if (MatchEffect(data.m_shared.m_setStatusEffect,
                        raw, localized, null, null, false,
                        out compactText, out icon))
                        return true;
                }
            }

            return false;
        }

        private static bool MatchEffect(StatusEffect effect,
            string raw, string localized, string equipmentName,
            Sprite equipmentIcon, bool isEquipment,
            out string compactText, out Sprite icon)
        {
            compactText = null;
            icon = null;
            if (effect == null)
                return false;

            bool started = Matches(effect.m_startMessage, raw, localized);
            bool stopped = Matches(effect.m_stopMessage, raw, localized);
            bool repeated = Matches(effect.m_repeatMessage, raw, localized);
            if (!started && !stopped && !repeated)
                return false;

            string label = !string.IsNullOrWhiteSpace(equipmentName)
                ? equipmentName
                : Localize(effect.m_name);
            if (string.IsNullOrWhiteSpace(label) || label[0] == '$')
                label = effect.name;
            if (string.IsNullOrWhiteSpace(label))
                return false;

            icon = equipmentIcon != null ? equipmentIcon : effect.m_icon;
            label = label.Trim();
            if (label.StartsWith("SE_", StringComparison.OrdinalIgnoreCase))
                label = label.Substring(3).Replace('_', ' ');

            // A translated effect name is a safe compact start label.
            // For removal, preserve the game's translated stop message
            // instead of inventing an English opposite.
            if (!CompactLocalization.IsEnglish)
            {
                compactText = stopped ? localized : label;
                return true;
            }

            // Shorten the specific full sentence, not the underlying
            // condition into an unrelated generic "ended".
            string concise = CompactFormat.SimplifyStatusSentence(localized);
            if (!string.Equals(concise, localized, StringComparison.Ordinal))
            {
                compactText = concise;
                return true;
            }

            if (isEquipment)
            {
                compactText = label + (stopped ? " removed" : " equipped");
                return true;
            }

            if (stopped)
            {
                if (label.IndexOf("cold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf("freezing", StringComparison.OrdinalIgnoreCase) >= 0)
                    compactText = "Warming up";
                else if (label.IndexOf("wet", StringComparison.OrdinalIgnoreCase) >= 0)
                    compactText = "Dry";
                else if (label.IndexOf("poison", StringComparison.OrdinalIgnoreCase) >= 0)
                    compactText = "No longer poisoned";
                else
                    compactText = "No longer " + label;
                return true;
            }

            compactText = label;
            return true;
        }

        private static bool Matches(string eventText, string raw,
            string localized)
        {
            if (string.IsNullOrWhiteSpace(eventText))
                return false;

            string token = eventText.Trim();
            if (string.Equals(token, (raw ?? "").Trim(),
                StringComparison.Ordinal))
                return true;

            return string.Equals(Localize(token).Trim(),
                (localized ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Localize(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";
            return Localization.instance == null
                ? token : Localization.instance.Localize(token);
        }
    }
}
