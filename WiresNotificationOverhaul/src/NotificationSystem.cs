using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WiresNotificationOverhaul
{
    internal static class NotificationSystem
    {
        private sealed class Notice
        {
            internal string Text;
            internal string Original;
            internal string CompactOverride;
            internal CompactFormat.Kind Kind;
            internal Sprite Icon;
            internal string UnlockCategory;
            internal List<Sprite> UnlockIcons;
            internal int UnlockIconIndex;
            internal float NextUnlockIconTime;            internal int Amount;
            internal bool IsXp;
            internal bool IsCenter;
            internal bool ForceCenterColor;
            internal bool IsFoodStats;
            internal float FoodHealth;
            internal float FoodStamina;
            internal float FoodEitr;
            internal Skills.SkillType SkillType;
            internal float XpAmount;
            internal float CurrentXp;
            internal float RequiredXp;
            internal bool HasXpProgress;
            internal int Revision;
        }

        private sealed class EntryView
        {
            internal GameObject Root;
            internal Image Icon;
            internal LayoutElement IconLayout;
            internal LayoutElement TextLayout;
            internal TMP_Text Text;
            internal string LastMeasuredText;
            internal float LastMeasuredIconSize = -1f;
            internal Notice RenderedNotice;
            internal int RenderedRevision = -1;
            internal int RenderedAppearanceRevision = -1;

            internal bool IsAlive =>
                Root != null &&
                Icon != null &&
                IconLayout != null &&
                Text != null;
        }

        private const float FadeDuration = 1f;

        private static readonly Regex FoodHealthRegex =
            new Regex(@"\+([0-9]+(?:[\.,][0-9]+)?)\s+\$item_food_health", RegexOptions.Compiled);

        private static readonly Regex FoodStaminaRegex =
            new Regex(@"\+([0-9]+(?:[\.,][0-9]+)?)\s+\$item_food_stamina", RegexOptions.Compiled);

        private static readonly Regex FoodEitrRegex =
            new Regex(@"\+([0-9]+(?:[\.,][0-9]+)?)\s+\$item_food_eitr", RegexOptions.Compiled);

        private static readonly LinkedList<Notice> ActiveNotifications =
            new LinkedList<Notice>();

        private static readonly List<EntryView> Entries =
            new List<EntryView>();

        private static MessageHud _hud;
        private static GameObject _panel;
        private static CanvasGroup _panelGroup;
        private static Canvas _panelCanvas;
        private static bool _inventoryWasVisible;
        private static RectTransform _azuQuickbarRect;
        private static float _timer;

        // A static notification can remain visible for several seconds.
        // Do not reassign TextMeshPro/UI LayoutElement properties on
        // every frame: doing so repeatedly dirties the Canvas layout.
        private static int _appearanceRevision;

        // Cache crosshair/stagger geometry: no global UI search or
        // RectTransform bounds traversal every notification frame.
        private static float _nextCrosshairSampleTime;
        private static Vector2 _cachedCrosshairPosition = new Vector2(-85f, 10f);
        private static float _nextEdgePaddingSampleTime;
        private static Vector2 _cachedBottomRightPadding = new Vector2(36f, 36f);

        private static bool IsRightAligned()
        {
            // The preset sets this config entry once. It never forces a
            // different result after the player manually edits alignment.
            return WiresNotificationOverhaulPlugin.NotificationAlignment.Value ==
                WiresNotificationOverhaulPlugin.TextAlignment.Right;
        }
        internal static bool TryHandleMessage(
            MessageHud hud,
            MessageHud.MessageType type,
            string text,
            int amount,
            Sprite icon,
            bool showDespiteHiddenHud,
            bool isRepair = false)
        {
            bool topLeft =
                type == MessageHud.MessageType.TopLeft;

            bool center =
                type == MessageHud.MessageType.Center;

            if (!topLeft && !center)
                return false;

            if (center &&
                WiresNotificationOverhaulPlugin
                    .MoveCenterNotifications.Value !=
                WiresNotificationOverhaulPlugin.Toggle.On)
            {
                return false;
            }

            if (Hud.IsUserHidden() &&
                !showDespiteHiddenHud)
                return true;

            if (WiresNotificationOverhaulPlugin
                    .PickupNotificationsOnly.Value ==
                WiresNotificationOverhaulPlugin.Toggle.On &&
                (string.IsNullOrWhiteSpace(text) ||
                 icon == null ||
                 amount < 1))
            {
                return false;
            }

            if (!EnsurePanel(hud))
                return false;

            if (center &&
                TryParseFoodStats(
                    text,
                    out float foodHealth,
                    out float foodStamina,
                    out float foodEitr))
            {
                Notice foodNotice =
                    FindFoodStats();

                if (foodNotice == null)
                {
                    if (ActiveNotifications.Count >=
                        WiresNotificationOverhaulPlugin
                            .MaxVisibleNotifications.Value)
                    {
                        ActiveNotifications.RemoveFirst();
                    }

                    foodNotice =
                        new Notice
                        {
                            IsCenter = true,
                            ForceCenterColor = true,
                            IsFoodStats = true
                        };

                    ActiveNotifications.AddLast(
                        foodNotice);
                }

                foodNotice.FoodHealth += foodHealth;
                foodNotice.FoodStamina += foodStamina;
                foodNotice.FoodEitr += foodEitr;
                foodNotice.Revision++;

                _timer = 0f;

                if (_panelGroup != null)
                    _panelGroup.alpha = 1f;

                return true;
            }

            string localized =
                Localization.instance != null
                    ? Localization.instance.Localize(text)
                    : text;



            // Classify once, then reuse it for unlocks, fortified
            // skill history and the displayed notification.
            CompactFormat.Kind kind =
                CompactFormat.Detect(text, localized);

            // Once grouped unlocks are active, the vanilla leftover
            // "N new unlocks" toast must never reappear.
            if (UnlockCapture.Enabled &&
                kind == CompactFormat.Kind.NewUnlocks)
                return true;

            // FortifySkillsRedux requests log=false when leveling.
            // Still put its skill event into Valheim's actual
            // message history with only the number tinted blue.
            if (kind == CompactFormat.Kind.FortifiedLevel)

            {
                List<string> history = hud.GetLog();
                if (history != null)
                {
                    history.Add(CompactFormat.Text(
                        CompactFormat.Kind.FortifiedLevel,
                        text, localized, amount, false,
                        SelectionBlue()));
                    while (history.Count > hud.m_maxLogMessages)
                        history.RemoveAt(0);
                }
            }

            Notice existing =
                FindExisting(localized, center);

            if (existing != null)
            {
                existing.Amount += amount;
                existing.Revision++;
            }
            else
            {
                if (ActiveNotifications.Count >=
                    WiresNotificationOverhaulPlugin
                        .MaxVisibleNotifications.Value)
                {
                    ActiveNotifications.RemoveFirst();
                }


                string compactOverride = null;
                Sprite matchedIcon = null;
                if (isRepair)
                {
                    // The verbose notification and vanilla message
                    // history retain "Repaired <item name>". In Compact
                    // Mode the UI renders the actual item icon and
                    // simply "repaired", without repeating its name.
                    compactOverride = CompactLocalization.IsEnglish
                        ? "repaired" : localized;
                }
                else if (kind == CompactFormat.Kind.Other)
                {
                    StatusMessageFormatter.TryResolve(
                        text, localized,
                        out compactOverride, out matchedIcon);

                    // A stopped Wet effect is no longer Wet. Do not
                    // invent an icon for Dry or reuse Wet droplets.
                    // A genuine icon supplied by vanilla is preserved.
                    if (icon == null && string.Equals(compactOverride,
                        "Dry", StringComparison.OrdinalIgnoreCase))
                        matchedIcon = null;
                }

                ActiveNotifications.AddLast(
                    new Notice
                    {
                        Text = localized,
                        Original = text,
                        CompactOverride = compactOverride,
                        Kind = kind,
                        Icon = ContextIcons.Choose(
                            text, localized,
                            compactOverride ?? localized,
                            icon != null ? icon : matchedIcon),
                        Amount = amount,
                        IsCenter = center
                    });
            }

            _timer = 0f;

            if (_panelGroup != null)
                _panelGroup.alpha = 1f;

            return true;
        }

        internal static bool TryShowGroupedUnlock(
            MessageHud hud, string category, Sprite icon)
        {
            if (hud == null || Hud.IsUserHidden() ||
                !EnsurePanel(hud))
                return false;

            if (string.IsNullOrWhiteSpace(category))
                category = CompactLocalization.NativeToken(
                    "$inventory_logs_new", "Discoveries");

            Notice grouped = null;
            for (LinkedListNode<Notice> node = ActiveNotifications.First;
                 node != null; node = node.Next)
            {
                if (node.Value.UnlockCategory == category)
                {
                    grouped = node.Value;
                    break;
                }
            }

            if (grouped == null)
            {
                if (ActiveNotifications.Count >=
                    WiresNotificationOverhaulPlugin
                        .MaxVisibleNotifications.Value)
                    ActiveNotifications.RemoveFirst();

                grouped = new Notice
                {
                    UnlockCategory = category,
                    UnlockIcons = new List<Sprite>(),
                    Amount = 0,
                    Text = "",
                    Original = ""
                };
                ActiveNotifications.AddLast(grouped);
            }

            grouped.Amount++;
            string unlocks = CompactLocalization.IsEnglish
                ? (grouped.Amount == 1 ? "new unlock" : "new unlocks")
                : CompactLocalization.NativeToken(
                    "$inventory_logs_new", category);
            grouped.Text = grouped.Amount + " " + unlocks +
                " (" + category + ")";
            grouped.Original = grouped.Text;

            if (icon != null)
            {
                grouped.UnlockIcons.Add(icon);
                if (grouped.UnlockIcons.Count == 1)
                    grouped.Icon = icon;
                if (grouped.UnlockIcons.Count == 2)
                    grouped.NextUnlockIconTime = Time.unscaledTime + 0.5f;
            }

            grouped.Revision++;
            _timer = 0f;
            if (_panelGroup != null)
                _panelGroup.alpha = 1f;
            return true;
        }

        internal static void ShowPotionEffectEnded(
            string itemNameToken,
            Sprite icon,
            bool available)
        {
            if (MessageHud.instance == null ||
                Hud.IsUserHidden())
                return;

            string localized =
                Localization.instance != null
                    ? Localization.instance.Localize(itemNameToken)
                    : itemNameToken;

            string message;
            if (!CompactLocalization.IsEnglish)
            {
                // There is no universal translated short form for
                // "available" / "expired" in Valheim. Keep the native
                // item name and use an unambiguous language-neutral sign.
                message = (available ? "+ " : "- ") + localized;
            }
            else
            {
                message = string.IsNullOrWhiteSpace(localized)
                    ? (available ? "Potion available" : "Potion expired")
                    : localized + (available ? " available" : " expired");
            }


            if (WiresNotificationOverhaulPlugin
                    .MoveCenterNotifications.Value !=
                WiresNotificationOverhaulPlugin.Toggle.On)
            {
                MessageHud.instance.ShowMessage(
                    MessageHud.MessageType.Center,
                    message,
                    0,
                    icon,
                    false);
                return;
            }

            if (!EnsurePanel(MessageHud.instance))
                return;

            if (ActiveNotifications.Count >=
                WiresNotificationOverhaulPlugin
                    .MaxVisibleNotifications.Value)
            {
                ActiveNotifications.RemoveFirst();
            }

            ActiveNotifications.AddLast(
                new Notice
                {
                    Text = message,
                    Original = message,
                    Kind = available
                        ? CompactFormat.Kind.PotionAvailable
                        : CompactFormat.Kind.PotionExpired,
                    Icon = icon,
                    Amount = 0,
                    IsCenter = true,
                    ForceCenterColor = true
                });

            _timer = 0f;

            if (_panelGroup != null)
                _panelGroup.alpha = 1f;
        }

        // One decimal, halves rounded up (50.25% shows as 50.3%).
        private static string Percent(float current, float required)
        {
            decimal value = (decimal)current * 100m / (decimal)required;
            value = Math.Round(value, 1, MidpointRounding.AwayFromZero);
            if (value > 100m) value = 100m;
            if (value < 0m) value = 0m;
            return value.ToString("0.0", CultureInfo.CurrentCulture) + "%";
        }

        internal static void ShowSkillXp(
            Skills.SkillDef skill,
            float amount,
            float currentXp,
            float requiredXp,
            bool hasXpProgress)
        {
            if (skill == null ||
                amount <= 0f ||
                MessageHud.instance == null ||
                Hud.IsUserHidden())
                return;

            if (!EnsurePanel(MessageHud.instance))
                return;

            Notice existing =
                FindExistingXp(skill.m_skill);

            if (existing != null)
            {
                existing.XpAmount += amount;
                existing.CurrentXp = currentXp;
                existing.RequiredXp = requiredXp;
                existing.HasXpProgress = hasXpProgress;
                existing.Revision++;
            }
            else
            {
                if (ActiveNotifications.Count >=
                    WiresNotificationOverhaulPlugin
                        .MaxVisibleNotifications.Value)
                {
                    ActiveNotifications.RemoveFirst();
                }

                string token =
                    "$skill_" +
                    skill.m_skill
                        .ToString()
                        .ToLowerInvariant();

                string localized =
                    Localization.instance != null
                        ? Localization.instance.Localize(token)
                        : skill.m_skill.ToString();

                ActiveNotifications.AddLast(
                    new Notice
                    {
                        Text = localized,
                        Icon = skill.m_icon,
                        Amount = 0,
                        IsXp = true,
                        SkillType = skill.m_skill,
                        XpAmount = amount,
                        CurrentXp = currentXp,
                        RequiredXp = requiredXp,
                        HasXpProgress = hasXpProgress
                    });
            }

            _timer = 0f;

            if (_panelGroup != null)
                _panelGroup.alpha = 1f;
        }

        internal static void Tick(
            MessageHud hud,
            float dt)
        {
            WiresNotificationOverhaulPlugin.CompleteLegacyPresetMigrationWhenReady();
            if (ActiveNotifications.Count == 0)
            {
                HidePanel();
                return;
            }

            if (!EnsurePanel(hud))
                return;

            // Inventory no longer pauses or hides notifications.
            // Only recheck the overlay draw order on open/close transitions,
            // instead of manipulating the Canvas every game frame.
            bool inventoryVisible = InventoryGui.IsVisible();
            if (inventoryVisible != _inventoryWasVisible)
            {
                _inventoryWasVisible = inventoryVisible;
                KeepBehindInventoryPanels();
            }

            _timer += dt;
            UpdatePanelPosition();
            RefreshEntries(hud);

            float duration =
                WiresNotificationOverhaulPlugin
                    .NotificationDuration.Value;

            float fadeStart =
                duration - FadeDuration;

            float targetAlpha;

            if (_timer < fadeStart)
            {
                targetAlpha = 1f;
            }
            else if (_timer >= duration)
            {
                targetAlpha = 0f;
            }
            else
            {
                targetAlpha =
                    Mathf.Lerp(
                        1f,
                        0f,
                        Mathf.InverseLerp(
                            fadeStart,
                            duration,
                            _timer));
            }

            if (_panelGroup != null)
            {
                _panelGroup.alpha =
                    Mathf.MoveTowards(
                        _panelGroup.alpha,
                        targetAlpha,
                        dt / FadeDuration);
            }

            if (_timer >= duration)
                ClearNotifications();
        }

        private static Notice FindExisting(
            string localizedText,
            bool isCenter)
        {
            LinkedListNode<Notice> node =
                ActiveNotifications.First;

            while (node != null)
            {
                if (!node.Value.IsXp &&
                    !node.Value.IsFoodStats &&
                    node.Value.UnlockCategory == null &&
                    node.Value.IsCenter == isCenter &&
                    node.Value.Text == localizedText)
                    return node.Value;

                node = node.Next;
            }

            return null;
        }

        private static Notice FindFoodStats()
        {
            LinkedListNode<Notice> node =
                ActiveNotifications.First;

            while (node != null)
            {
                if (node.Value.IsFoodStats)
                    return node.Value;

                node = node.Next;
            }

            return null;
        }

        private static bool TryParseFoodStats(
            string text,
            out float health,
            out float stamina,
            out float eitr)
        {
            health = 0f;
            stamina = 0f;
            eitr = 0f;

            if (string.IsNullOrWhiteSpace(text) ||
                text.IndexOf("$item_food_", StringComparison.Ordinal) < 0)
                return false;

            bool found = false;

            found |= TryParseFoodValue(
                FoodHealthRegex,
                text,
                out health);

            found |= TryParseFoodValue(
                FoodStaminaRegex,
                text,
                out stamina);

            found |= TryParseFoodValue(
                FoodEitrRegex,
                text,
                out eitr);

            return found;
        }

        private static bool TryParseFoodValue(
            Regex regex,
            string text,
            out float value)
        {
            value = 0f;

            Match match =
                regex.Match(text);

            if (!match.Success ||
                match.Groups.Count < 2)
                return false;

            string number =
                match.Groups[1].Value;

            return
                float.TryParse(
                    number,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out value) ||
                float.TryParse(
                    number.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private static Notice FindExistingXp(
            Skills.SkillType skillType)
        {
            LinkedListNode<Notice> node =
                ActiveNotifications.First;

            while (node != null)
            {
                if (node.Value.IsXp &&
                    node.Value.SkillType == skillType)
                    return node.Value;

                node = node.Next;
            }

            return null;
        }
        private static bool EnsurePanel(
            MessageHud hud)
        {
            if (hud == null ||
                hud.m_messageText == null)
                return false;

            if (_hud != hud)
                ResetForHud(hud);

            if (_panel != null &&
                _panelGroup != null)
                return true;

            Entries.Clear();

            _panel =
                new GameObject(
                    "WireNotificationPanel",
                    typeof(RectTransform),
                    typeof(CanvasGroup));

            _panel.transform.SetParent(
                hud.transform,
                false);

            // A separate non-interactive canvas allows the notification
            // text/icons to remain visible but render BELOW the inventory
            // wooden panels, even when both UI systems share a root canvas.
            _panelCanvas = _panel.AddComponent<Canvas>();
            _panelCanvas.overrideSorting = true;
            _panelGroup = _panel.GetComponent<CanvasGroup>();
            _panelGroup.blocksRaycasts = false;
            _panelGroup.interactable = false;
            _inventoryWasVisible = InventoryGui.IsVisible();
            KeepBehindInventoryPanels();

            RectTransform rect =
                _panel.GetComponent<RectTransform>();

            rect.anchorMin =
                new Vector2(0f, 1f);

            rect.anchorMax =
                new Vector2(0f, 1f);

            rect.pivot =
                new Vector2(0f, 1f);

            rect.sizeDelta =
                new Vector2(400f, 32f);

            VerticalLayoutGroup layout =
                _panel.AddComponent<VerticalLayoutGroup>();

            layout.spacing = 4f;
            layout.padding =
                new RectOffset(0, 0, 0, 0);

            layout.childAlignment =
                TextAnchor.UpperLeft;

            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;

            _panelGroup.alpha = 0f;
            UpdatePanelPosition();

            return true;
        }

        private static void KeepBehindInventoryPanels()
        {
            if (_panelCanvas == null)
                return;

            // The inventory may have its own Canvas, or share the HUD
            // overlay Canvas. In either case, an explicit lower canvas
            // sorting order keeps notifications UNDER its wood panels,
            // without clearing/hiding the notices when inventory opens.
            Canvas inventoryCanvas = null;
            InventoryGui inventory = InventoryGui.instance;
            if (inventory != null && inventory.m_inventoryRoot != null)
                inventoryCanvas = inventory.m_inventoryRoot.GetComponentInParent<Canvas>();

            Canvas hudCanvas = null;
            if (_panelCanvas.transform.parent != null)
                hudCanvas = _panelCanvas.transform.parent.GetComponentInParent<Canvas>();

            Canvas reference = inventoryCanvas != null ? inventoryCanvas : hudCanvas;
            int targetOrder = reference != null ? reference.sortingOrder - 1 : -1;
            if (_panelCanvas.sortingOrder != targetOrder)
                _panelCanvas.sortingOrder = targetOrder;
        }

        private static void ResetForHud(
            MessageHud hud)
        {
            if (_panel != null)
                UnityEngine.Object.Destroy(_panel);

            _hud = hud;
            _panel = null;
            _panelGroup = null;
            _panelCanvas = null;
            _inventoryWasVisible = false;
            _azuQuickbarRect = null;
            _nextCrosshairSampleTime = 0f;
            _cachedCrosshairPosition = new Vector2(-85f, 10f);
            _nextEdgePaddingSampleTime = 0f;
            _cachedBottomRightPadding = new Vector2(36f, 36f);
            Entries.Clear();
            ActiveNotifications.Clear();
            _timer = 0f;
        }
        private static EntryView CreateEntry(
            MessageHud hud)
        {
            GameObject root =
                new GameObject(
                    "WireNotificationEntry",
                    typeof(RectTransform));

            root.transform.SetParent(
                _panel.transform,
                false);

            root.GetComponent<RectTransform>()
                .sizeDelta =
                    new Vector2(400f, 32f);

            HorizontalLayoutGroup layout =
                root.AddComponent<HorizontalLayoutGroup>();

            layout.spacing = 8f;
            layout.padding =
                new RectOffset(0, 0, 0, 0);

            layout.childAlignment =
                TextAnchor.MiddleLeft;

            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            GameObject iconObject =
                new GameObject(
                    "Icon",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));

            iconObject.transform.SetParent(
                root.transform,
                false);

            iconObject.GetComponent<RectTransform>()
                .sizeDelta =
                    new Vector2(28f, 28f);

            Image icon =
                iconObject.GetComponent<Image>();

            LayoutElement iconLayout =
                iconObject.AddComponent<LayoutElement>();

            GameObject textObject =
                UnityEngine.Object.Instantiate(
                    hud.m_messageText.gameObject,
                    root.transform,
                    false);

            textObject.name = "Text";

            RectTransform textRect =
                textObject.GetComponent<RectTransform>();

            if (textRect != null)
                textRect.anchorMin = Vector2.zero;

            TMP_Text text =
                textObject.GetComponent<TMP_Text>();

            // Give the horizontal layout a predictable text width in
            // either icon order, without allowing icons to overflow.
            LayoutElement textLayout =
                textObject.GetComponent<LayoutElement>() ??
                textObject.AddComponent<LayoutElement>();
            textLayout.minWidth = 0f;
            textLayout.preferredWidth = 360f;
            textLayout.flexibleWidth = 0f;
            textLayout.preferredHeight = 32f;
            textLayout.flexibleHeight = 0f;

            EntryView view = new EntryView
            {
                Root = root,
                Icon = icon,
                IconLayout = iconLayout,
                TextLayout = textLayout,
                Text = text
            };
            ApplyIconLayout(view);
            return view;
        }
        private static void RefreshEntries(
            MessageHud hud)
        {
            for (int i = Entries.Count - 1;
                 i >= 0;
                 i--)
            {
                if (!Entries[i].IsAlive)
                    Entries.RemoveAt(i);
            }

            int index = 0;

            // BottomRight reverses the stack: newest appears above older
            // messages, while exiting content slides down toward the edge.
            bool reverse = WiresNotificationOverhaulPlugin.Placement.Value ==
                WiresNotificationOverhaulPlugin.PositionPreset.BottomRight;
            LinkedListNode<Notice> node = reverse
                ? ActiveNotifications.Last : ActiveNotifications.First;

            while (node != null)
            {
                EntryView view;

                if (index < Entries.Count)
                {
                    view = Entries[index];
                }
                else
                {
                    view = CreateEntry(hud);
                    Entries.Add(view);
                }

                Notice notice = node.Value;
                // Only the small Image sprite changes during unlock
                // cycling. Never rebuild TMP text, hierarchy, or layout
                // every 0.5 s (especially important for large batches).
                if (notice.UnlockIcons != null &&
                    notice.UnlockIcons.Count > 1 &&
                    Time.unscaledTime >= notice.NextUnlockIconTime)
                {
                    notice.UnlockIconIndex =
                        (notice.UnlockIconIndex + 1) %
                            notice.UnlockIcons.Count;
                    notice.Icon = notice.UnlockIcons[notice.UnlockIconIndex];
                    notice.NextUnlockIconTime =
                        Time.unscaledTime + 0.5f;
                    if (view.RenderedNotice == notice &&
                        view.Icon != null)
                        view.Icon.sprite = notice.Icon;
                }

                if (view.RenderedNotice != notice ||
                    view.RenderedRevision != notice.Revision ||
                    view.RenderedAppearanceRevision != _appearanceRevision)
                {
                    UpdateEntry(view, notice);
                    view.RenderedNotice = notice;
                    view.RenderedRevision = notice.Revision;
                    view.RenderedAppearanceRevision = _appearanceRevision;
                }

                if (!view.Root.activeSelf)
                    view.Root.SetActive(true);

                index++;
                node = reverse ? node.Previous : node.Next;
            }

            for (int i = index;
                 i < Entries.Count;
                 i++)
            {
                if (Entries[i].Root != null &&
                    Entries[i].Root.activeSelf)
                    Entries[i].Root.SetActive(false);
            }

            if (_panel != null)
            {
                // The right/bottom and center anchors need the actual
                // stack height so it grows in the intended direction.
                RectTransform panelRect = _panel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    float rowHeight = Mathf.Max(32f,
                        WiresNotificationOverhaulPlugin.IconSize.Value);
                    float height = Mathf.Max(32f,
                        index * (rowHeight + 4f) - 4f);
                    if (!Mathf.Approximately(panelRect.sizeDelta.y, height))
                        panelRect.sizeDelta = new Vector2(400f, height);
                }
                if (!_panel.activeSelf)
                    _panel.SetActive(true);
            }
        }

        private static void ApplyIconLayout(EntryView view)
        {
            if (view == null || !view.IsAlive)
                return;

            bool iconsOnRight = IsRightAligned();

            if (iconsOnRight)
                view.Icon.transform.SetAsLastSibling();
            else
                view.Icon.transform.SetAsFirstSibling();

            HorizontalLayoutGroup layout =
                view.Root.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.childAlignment =
                    iconsOnRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

            view.Text.alignment =
                iconsOnRight ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            float rowHeight = Mathf.Max(32f,
                WiresNotificationOverhaulPlugin.IconSize.Value);
            RectTransform rootRect = view.Root.GetComponent<RectTransform>();
            if (rootRect != null &&
                !Mathf.Approximately(rootRect.sizeDelta.y, rowHeight))
                rootRect.sizeDelta = new Vector2(400f, rowHeight);
        }

        // Called on config changes, including while notifications are visible.
        internal static void UpdateEntryLayout()
        {
            // Config changes invalidate the cached presentation once.
            // They do not turn stable notifications back into per-frame work.
            _appearanceRevision++;
            foreach (EntryView view in Entries)
            {
                ApplyIconLayout(view);
                view.RenderedAppearanceRevision = -1;
                view.LastMeasuredText = null;
                view.LastMeasuredIconSize = -1f;
            }
        }

        private static void UpdateEntry(
            EntryView view,
            Notice notice)
        {
            if (view == null ||
                !view.IsAlive)
                return;

            bool hasIcon = notice.Icon != null;
            if (view.Icon.enabled != hasIcon)
                view.Icon.enabled = hasIcon;

            if (view.Icon.sprite != notice.Icon)
                view.Icon.sprite = notice.Icon;

            view.Icon.preserveAspect = true;
            view.Icon.pixelsPerUnitMultiplier = 1f;

            float iconSize = WiresNotificationOverhaulPlugin.IconSize.Value;
            RectTransform iconRect = view.Icon.rectTransform;
            if (iconRect != null &&
                (!Mathf.Approximately(iconRect.sizeDelta.x, iconSize) ||
                 !Mathf.Approximately(iconRect.sizeDelta.y, iconSize)))
            {
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            }
            if (!Mathf.Approximately(view.IconLayout.preferredWidth, iconSize))
                view.IconLayout.preferredWidth = iconSize;
            if (!Mathf.Approximately(view.IconLayout.preferredHeight, iconSize))
                view.IconLayout.preferredHeight = iconSize;
            if (!Mathf.Approximately(view.IconLayout.minWidth, iconSize))
                view.IconLayout.minWidth = iconSize;
            if (!Mathf.Approximately(view.IconLayout.minHeight, iconSize))
                view.IconLayout.minHeight = iconSize;
            if (!Mathf.Approximately(view.IconLayout.flexibleWidth, 0f))
                view.IconLayout.flexibleWidth = 0f;
            if (!Mathf.Approximately(view.IconLayout.flexibleHeight, 0f))
                view.IconLayout.flexibleHeight = 0f;

            float availableTextWidth = Mathf.Max(220f, 400f - iconSize - 12f);
            if (view.TextLayout != null)
            {
                if (!Mathf.Approximately(
                        view.TextLayout.preferredWidth, availableTextWidth))
                    view.TextLayout.preferredWidth = availableTextWidth;
                float height = Mathf.Max(32f, iconSize);
                if (!Mathf.Approximately(
                        view.TextLayout.preferredHeight, height))
                    view.TextLayout.preferredHeight = height;
            }

            float fontSize = WiresNotificationOverhaulPlugin
                .NotificationsFontSize.Value;
            if (!Mathf.Approximately(view.Text.fontSize, fontSize))
                view.Text.fontSize = fontSize;

            Color textColor =
                WiresNotificationOverhaulPlugin
                    .NotificationTextColor.Value;

            if (notice.IsCenter &&
                (notice.ForceCenterColor ||
                 WiresNotificationOverhaulPlugin
                    .PreserveCenterMessageColor.Value ==
                 WiresNotificationOverhaulPlugin.Toggle.On) &&
                _hud != null &&
                _hud.m_messageCenterText != null)
            {
                textColor = _hud.m_messageCenterText.color;
            }

            if (view.Text.color != textColor)
                view.Text.color = textColor;

            view.Text.textWrappingMode =
                TextWrappingModes.NoWrap;
            view.Text.richText = true;

            bool compact =
                WiresNotificationOverhaulPlugin.CompactMode.Value ==
                WiresNotificationOverhaulPlugin.Toggle.On;

            if (notice.UnlockCategory != null)
            {
                // Count already belongs to the category sentence.
                // Never append an erroneous " xN" to unlock groups.
                view.Text.text = notice.Text;
            }
            else if (notice.IsFoodStats)
            {
                // Food stats always appear health, stamina, then eitr.
                // The HUD bar colors make the numbers self-explanatory;
                // do not append resource names in either display mode.

                string foodText = "";
                if (notice.FoodHealth > 0f)
                    foodText += "<color=#D95056>+" +
                        notice.FoodHealth.ToString("0.##",
                            CultureInfo.CurrentCulture) + "</color>";
                if (notice.FoodStamina > 0f)
                    foodText += (foodText.Length > 0 ? "  " : "") +
                        "<color=#E7C745>+" +
                        notice.FoodStamina.ToString("0.##",
                            CultureInfo.CurrentCulture) + "</color>";
                if (notice.FoodEitr > 0f)
                    foodText += (foodText.Length > 0 ? "  " : "") +
                        "<color=#B879E2>+" +
                        notice.FoodEitr.ToString("0.##",
                            CultureInfo.CurrentCulture) + "</color>";
                view.Text.text = foodText;
            }
            else if (notice.IsXp)
            {
                bool progress =
                    WiresNotificationOverhaulPlugin
                        .XpProgressNotifications.Value ==
                    WiresNotificationOverhaulPlugin.Toggle.On &&
                    notice.HasXpProgress;

                // XP has exactly one lowercase xp suffix and a plus
                // sign ONLY when displaying gained XP. The same
                // number syntax is used in both display modes.
                bool percent =
                    WiresNotificationOverhaulPlugin
                        .XpProgressPercent.Value ==
                    WiresNotificationOverhaulPlugin.Toggle.On &&
                    notice.HasXpProgress && notice.RequiredXp > 0f;
                string number = percent
                    ? Percent(notice.CurrentXp, notice.RequiredXp)
                    : progress
                    ? $"{notice.CurrentXp:0.##}/{notice.RequiredXp:0.##}xp"
                    : $"+{notice.XpAmount:0.##}xp";
                view.Text.text = compact
                    ? number : notice.Text + " " + number;
            }
            else
            {
                view.Text.text = compact &&
                    !string.IsNullOrWhiteSpace(notice.CompactOverride)
                        ? notice.CompactOverride
                        : CompactFormat.Text(
                            notice.Kind, notice.Original,
                            notice.Text, notice.Amount, compact,
                            notice.Kind == CompactFormat.Kind.FortifiedLevel
                                ? SelectionBlue() : "#79B9F0");
            }

            // Right-align the content as a group as well as the text:
            // icons and short messages should sit together immediately
            // to the left of the crosshair, not fill a 400-unit row.
            // Recalculate text width only when the message or icon
            // size changes, never unconditionally every frame.
            if (IsRightAligned())
            {
                string renderedText = view.Text.text;
                if (!string.Equals(view.LastMeasuredText, renderedText,
                        StringComparison.Ordinal) ||
                    !Mathf.Approximately(view.LastMeasuredIconSize, iconSize))
                {
                    view.LastMeasuredText = renderedText;
                    view.LastMeasuredIconSize = iconSize;
                    float width = view.Text.GetPreferredValues(renderedText).x;
                    if (view.TextLayout != null)
                        view.TextLayout.preferredWidth =
                            Mathf.Clamp(width + 4f, 24f,
                                Mathf.Max(220f, 400f - iconSize - 12f));
                }
            }
            else
            {
                // Restore the original left/right stack layout when the
                // user changes the placement at runtime.
                if (view.LastMeasuredText != null)
                {
                    view.LastMeasuredText = null;
                    view.LastMeasuredIconSize = -1f;
                    if (view.TextLayout != null)
                        view.TextLayout.preferredWidth =
                            Mathf.Max(220f, 400f - iconSize - 12f);
                }
            }
        }

        // Match Valheim's own inventory-selection blue whenever its
        // UI elements are available. Fallback is used only before
        // the inventory UI has initialized.
        private static string _selectedBlue;
        private static float _nextSelectionColorLookup;
        private const string FallbackSelectionBlue = "#79B9F0";

        private static string SelectionBlue()
        {
            if (_selectedBlue != null)
                return _selectedBlue;

            // FSR level-up messages request the same colored text on
            // multiple UI paths. An inventory without a blue selection
            // color must not trigger an expensive global object scan
            // every frame for the entire notification duration.
            if (Time.unscaledTime < _nextSelectionColorLookup)
                return FallbackSelectionBlue;
            _nextSelectionColorLookup = Time.unscaledTime + 8f;

            InventoryElement[] elements =
                Resources.FindObjectsOfTypeAll<InventoryElement>();
            foreach (InventoryElement element in elements)
            {
                if (element == null)
                    continue;

                Color blue = element.HighlightOriginalColor;
                if (blue.b > blue.r + 0.08f &&
                    blue.b >= blue.g &&
                    blue.a > 0.25f)
                {
                    _selectedBlue = "#" +
                        ColorUtility.ToHtmlStringRGB(blue);
                    return _selectedBlue;
                }

                // Some Valheim UI versions tint the selection overlay
                // instead of the button's highlighted ColorBlock.
                if (element.m_selected != null)
                {
                    Image selected =
                        element.m_selected.GetComponent<Image>();
                    if (selected != null)
                    {
                        blue = selected.color;
                        if (blue.b > blue.r + 0.08f &&
                            blue.b >= blue.g &&
                            blue.a > 0.25f)
                        {
                            _selectedBlue = "#" +
                                ColorUtility.ToHtmlStringRGB(blue);
                            return _selectedBlue;
                        }
                    }
                }
            }

            return FallbackSelectionBlue;
        }

        private static float GetAzuQuickbarOffset()
        {
            if (_azuQuickbarRect == null)
            {
                Hud hud = Hud.instance;

                if (hud == null ||
                    hud.m_rootObject == null)
                    return 0f;

                Transform quickbar =
                    hud.m_rootObject.transform.Find(
                        "AzuEPI_QuickAccessBar");

                if (quickbar == null)
                    return 0f;

                _azuQuickbarRect =
                    quickbar.GetComponent<RectTransform>();
            }

            if (_azuQuickbarRect == null ||
                !_azuQuickbarRect.gameObject.activeInHierarchy ||
                _azuQuickbarRect.childCount == 0)
            {
                return 0f;
            }

            Bounds renderedBounds =
                RectTransformUtility
                    .CalculateRelativeRectTransformBounds(
                        _azuQuickbarRect);

            float rowHeight =
                Mathf.Max(
                    _azuQuickbarRect.rect.height,
                    renderedBounds.size.y);

            return Mathf.Max(
                0f,
                rowHeight *
                Mathf.Abs(_azuQuickbarRect.localScale.y) +
                8f);
        }

        private static Vector2 NearCrosshairPosition(RectTransform parentRect)
        {
            if (parentRect == null)
                return _cachedCrosshairPosition;
            // Sample only a few times a second to track HUD scale changes
            // without doing transform-bound calculations each frame.
            if (Time.unscaledTime < _nextCrosshairSampleTime)
                return _cachedCrosshairPosition;

            _nextCrosshairSampleTime = Time.unscaledTime + 0.4f;
            Hud hud = Hud.instance;
            if (hud == null || hud.m_crosshair == null)
                return _cachedCrosshairPosition;

            RectTransform crosshair = hud.m_crosshair.rectTransform;

            // The fill itself represents the stagger HUD's visible bar
            // more accurately than the animator/root, which may include
            // extra surrounding UI geometry.
            RectTransform stagger =
                hud.m_staggerProgress != null
                    ? hud.m_staggerProgress.GetComponent<RectTransform>()
                    : null;
            if (stagger == null && hud.m_staggerAnimator != null)
                stagger = hud.m_staggerAnimator.GetComponent<RectTransform>();

            // Convert both HUD references to the actual canvas coordinates.
            Bounds crossBounds = RectTransformUtility
                .CalculateRelativeRectTransformBounds(parentRect, crosshair);

            Vector2 parentCenter = parentRect.rect.center;
            float gap = 78f;
            float targetY = crossBounds.center.y - parentCenter.y;

            if (stagger != null)
            {
                Bounds barBounds = RectTransformUtility
                    .CalculateRelativeRectTransformBounds(parentRect, stagger);

                // Some HUD replacements animate or collapse the fill
                // transform while it is hidden. Fall back to the stagger
                // widget root rather than reverting to reticle-height Y.
                if ((barBounds.size.x < 4f || barBounds.size.y < 2f) &&
                    hud.m_staggerAnimator != null)
                {
                    RectTransform frame =
                        hud.m_staggerAnimator.GetComponent<RectTransform>();
                    if (frame != null)
                        barBounds = RectTransformUtility
                            .CalculateRelativeRectTransformBounds(parentRect, frame);
                }

                // Stagger bars can deliberately sit far below the crosshair,
                // depending on HUD layout. Never reject their geometry
                // because of vertical separation from the reticle.
                if (barBounds.size.x >= 4f && barBounds.size.y >= 2f)
                {
                    targetY = barBounds.center.y - parentCenter.y;

                    // Mirror the actual empty gap on the other side of
                    // the crosshair, whatever the bar's vertical placement.
                    float measuredGap = -1f;
                    if (barBounds.min.x >= crossBounds.max.x)
                        measuredGap = barBounds.min.x - crossBounds.max.x;
                    else if (barBounds.max.x <= crossBounds.min.x)
                        measuredGap = crossBounds.min.x - barBounds.max.x;

                    if (measuredGap >= 6f && measuredGap <= 420f)
                        gap = measuredGap;
                }
            }

            // The icon is to the RIGHT of the text in this preset.
            // Add its full width to the clearance so the icon, not
            // just the text, respects the stagger HUD's mirrored gap.
            float iconClearance = Mathf.Max(0f,
                WiresNotificationOverhaulPlugin.IconSize.Value);

            _cachedCrosshairPosition = new Vector2(
                crossBounds.min.x - parentCenter.x - gap - iconClearance,
                targetY);
            return _cachedCrosshairPosition;
        }

        private static Vector2 BottomRightPadding(RectTransform parentRect)
        {
            if (parentRect == null)
                return _cachedBottomRightPadding;

            if (Time.unscaledTime < _nextEdgePaddingSampleTime)
                return _cachedBottomRightPadding;

            _nextEdgePaddingSampleTime = Time.unscaledTime + 0.4f;

            Hud hud = Hud.instance;
            if (hud == null || hud.m_healthBarRoot == null)
                return _cachedBottomRightPadding;

            // Match the distance from the screen edge to the original
            // health HUD, not a made-up hotbar or quickbar offset.
            Bounds healthBounds = RectTransformUtility
                .CalculateRelativeRectTransformBounds(parentRect, hud.m_healthBarRoot);
            Rect parentBounds = parentRect.rect;

            float horizontalMargin =
                healthBounds.min.x - parentBounds.xMin;
            float verticalMargin =
                healthBounds.min.y - parentBounds.yMin;

            // Protect against a modded or temporarily invalid HUD
            // reporting bounds extending far beyond the visible canvas.
            if (horizontalMargin >= 12f && horizontalMargin <= 180f &&
                verticalMargin >= 12f && verticalMargin <= 180f)
            {
                _cachedBottomRightPadding =
                    new Vector2(horizontalMargin, verticalMargin);
            }
            return _cachedBottomRightPadding;
        }

        internal static Vector2 GetPresetCoordinates(
            WiresNotificationOverhaulPlugin.PositionPreset preset)
        {
            if (preset == WiresNotificationOverhaulPlugin.PositionPreset.Default)
                return new Vector2(40f, -160f);

            // Match the parent coordinate space used by alternate presets.
            MessageHud messages = _hud != null ? _hud : MessageHud.instance;
            RectTransform canvasRect = null;
            if (messages != null && messages.m_messageText != null &&
                messages.m_messageText.canvas != null &&
                messages.m_messageText.canvas.rootCanvas != null)
            {
                canvasRect = messages.m_messageText.canvas.rootCanvas
                    .GetComponent<RectTransform>();
            }

            if (preset == WiresNotificationOverhaulPlugin.PositionPreset.NearCrosshair)
            {
                _nextCrosshairSampleTime = 0f;
                return NearCrosshairPosition(canvasRect);
            }

            _nextEdgePaddingSampleTime = 0f;
            Vector2 margin = BottomRightPadding(canvasRect);

            // BottomRight also puts icons AFTER the right-aligned text.
            // Keep the icon's full width inside the safe screen margin.
            float iconClearance = Mathf.Max(0f,
                WiresNotificationOverhaulPlugin.IconSize.Value);
            return new Vector2(-margin.x - iconClearance, margin.y);
        }

        internal static void UpdatePanelPosition()
        {
            if (_panel == null)
                return;

            RectTransform rect = _panel.GetComponent<RectTransform>();
            if (rect == null)
                return;

            var preset = WiresNotificationOverhaulPlugin.Placement.Value;
            bool defaultPreset =
                preset == WiresNotificationOverhaulPlugin.PositionPreset.Default;

            // The legacy HUD node is not guaranteed to cover the whole
            // canvas. Non-default presets anchor to the actual full-screen
            // root canvas so BottomRight means the screen's bottom-right.
            Transform desiredParent = _hud != null ? _hud.transform : rect.parent;
            if (!defaultPreset && _hud != null && _hud.m_messageText != null &&
                _hud.m_messageText.canvas != null &&
                _hud.m_messageText.canvas.rootCanvas != null)
            {
                desiredParent = _hud.m_messageText.canvas.rootCanvas.transform;
            }
            if (desiredParent != null && rect.parent != desiredParent)
            {
                rect.SetParent(desiredParent, false);
                _nextCrosshairSampleTime = 0f;
                _nextEdgePaddingSampleTime = 0f;
                KeepBehindInventoryPanels();
            }

            Vector2 anchor;
            Vector2 pivot;

            switch (preset)
            {
                case WiresNotificationOverhaulPlugin.PositionPreset.NearCrosshair:
                    anchor = new Vector2(0.5f, 0.5f);
                    pivot = new Vector2(1f, 0.5f);
                    break;
                case WiresNotificationOverhaulPlugin.PositionPreset.BottomRight:
                    anchor = new Vector2(1f, 0f);
                    pivot = new Vector2(1f, 0f);
                    break;
                default:
                    anchor = new Vector2(0f, 1f);
                    pivot = new Vector2(0f, 1f);
                    break;
            }

            // Hard user coordinates, which selecting a preset writes ONCE.
            // No further overrides: the user can freely adjust both X and Y.
            Vector2 position =
                WiresNotificationOverhaulPlugin.NotificationsPosition.Value;

            if (preset == WiresNotificationOverhaulPlugin.PositionPreset.BottomRight)
            {
                float duration =
                    WiresNotificationOverhaulPlugin.NotificationDuration.Value;
                float fade = Mathf.Clamp01(
                    (_timer - Mathf.Max(0f, duration - FadeDuration)) /
                    Mathf.Max(0.01f, Mathf.Min(FadeDuration, duration)));
                // Keep the intentional DOWNWARD departure animation, but
                // apply it relative to the editable hard position.
                position.y -= 22f * fade;
            }
            if (rect.anchorMin != anchor)
                rect.anchorMin = anchor;
            if (rect.anchorMax != anchor)
                rect.anchorMax = anchor;
            if (rect.pivot != pivot)
                rect.pivot = pivot;
            if (rect.anchoredPosition != position)
                rect.anchoredPosition = position;

            VerticalLayoutGroup layout = _panel.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                TextAnchor alignment =
                    preset == WiresNotificationOverhaulPlugin.PositionPreset.BottomRight
                        ? (IsRightAligned() ? TextAnchor.LowerRight : TextAnchor.LowerLeft)
                        : (IsRightAligned() ? TextAnchor.UpperRight : TextAnchor.UpperLeft);
                if (layout.childAlignment != alignment)
                    layout.childAlignment = alignment;
            }
        }
        private static void HidePanel()
        {
            if (_panel != null)
                _panel.SetActive(false);
        }

        internal static void ClearNotifications()
        {
            ActiveNotifications.Clear();
            _timer = 0f;

            for (int i = 0;
                 i < Entries.Count;
                 i++)
            {
                if (Entries[i].Root != null &&
                    Entries[i].Root.activeSelf)
                    Entries[i].Root.SetActive(false);
            }

            HidePanel();

            if (_panelGroup != null)
                _panelGroup.alpha = 0f;
        }

        internal static void OnHudDestroyed(
            MessageHud hud)
        {
            if (_hud != hud)
                return;

            _hud = null;
            _panel = null;
            _panelGroup = null;
            _panelCanvas = null;
            _inventoryWasVisible = false;
            _azuQuickbarRect = null;
            _nextCrosshairSampleTime = 0f;
            _cachedCrosshairPosition = new Vector2(-85f, 10f);
            _nextEdgePaddingSampleTime = 0f;
            _cachedBottomRightPadding = new Vector2(36f, 36f);
            Entries.Clear();
            ActiveNotifications.Clear();
            _timer = 0f;
        }

        internal static void Shutdown()
        {
            ClearNotifications();

            if (_panel != null)
                UnityEngine.Object.Destroy(_panel);

            _hud = null;
            _panel = null;
            _panelGroup = null;
            _panelCanvas = null;
            _inventoryWasVisible = false;
            _azuQuickbarRect = null;
            Entries.Clear();
        }
    }
}
