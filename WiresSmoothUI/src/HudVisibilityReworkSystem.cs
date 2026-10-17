using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    internal static class HudVisibilityReworkSystem
    {
        internal const float FadeSeconds = 0.20f;
        internal const float HotbarHideDelay = 1.00f;
        internal const float ResourceHideDelay = 1.00f;
        internal const float ForsakenPreviewSeconds = 5.00f;
        internal const float FoodRecentRevealSeconds = 1.00f;
        internal const float FoodReplaceableRevealSeconds = 1.00f;
        internal const float FoodExpiryFlashSeconds = 60.00f;
        internal const float HotbarInstantEquipRevealSeconds = 0.75f;
        internal const float HotbarPostActionHoldSeconds = 0.25f;

        private static readonly AccessTools.FieldRef<HotkeyBar, List<ItemDrop.ItemData>>
            HotkeyItems =
                AccessTools.FieldRefAccess<HotkeyBar, List<ItemDrop.ItemData>>(
                    "m_items");

        private static readonly AccessTools.FieldRef<Humanoid, HashSet<StatusEffect>>
            EquipmentStatusEffects =
                AccessTools.FieldRefAccess<Humanoid, HashSet<StatusEffect>>(
                    "m_equipmentStatusEffects");

        private static readonly HashSet<int> EquipmentEffectHashes =
            new HashSet<int>();

        private static readonly Dictionary<int, Sprite> ForsakenUnavailableSprites =
            new Dictionary<int, Sprite>();

        private static readonly List<Texture2D> ForsakenUnavailableTextures =
            new List<Texture2D>();

        private static SmoothUIHudVisibilityReworkDriver _driver;
        private static float _foodRevealUntil = -999f;

        internal static void Register(
            SmoothUIHudVisibilityReworkDriver driver)
        {
            _driver = driver;
        }

        internal static void Unregister(
            SmoothUIHudVisibilityReworkDriver driver)
        {
            if (_driver == driver)
                _driver = null;
        }

        internal static void Shutdown()
        {
            _driver?.RestoreEverything();
            _driver = null;
            EquipmentEffectHashes.Clear();
            ClearForsakenUnavailableSprites();
            _foodRevealUntil = -999f;
        }

        internal static void NotifyFoodEaten(Player player)
        {
            if (player == null ||
                player != Player.m_localPlayer)
                return;

            _foodRevealUntil =
                Time.unscaledTime +
                FoodRecentRevealSeconds;
        }

        internal static bool FoodRecentlyEaten =>
            Time.unscaledTime < _foodRevealUntil;

        internal static bool IsCombatRelevant(Player player)
        {
            if (player == null)
                return false;

            // Combat visibility is enemy-awareness driven: show the
            // combat HUD only while an enemy is targeting or sensing
            // the player.
            return player.IsTargeted() ||
                   player.IsSensed();
        }

        internal static ItemDrop.ItemData ActiveEquipActionItem(
            Player player)
        {
            if (player == null)
                return null;

            player.GetActionProgress(
                out string _,
                out float _,
                out Player.MinorActionData action);

            if (action == null ||
                action.m_item == null)
                return null;

            if (action.m_type !=
                    Player.MinorActionData.ActionType.Equip &&
                action.m_type !=
                    Player.MinorActionData.ActionType.Unequip)
            {
                return null;
            }

            return action.m_item;
        }

        internal static void NotifyHotbarEquipmentChanged()
        {
            _driver?.NotifyHotbarEquipmentChanged();
        }

        internal static void FilterPermanentEquipmentEffects(
            List<StatusEffect> effects)
        {
            if (effects == null ||
                !WiresSmoothUIPlugin.Enabled.Value ||
                !WiresSmoothUIPlugin.HidePermanentStatusEffects.Value)
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            EquipmentEffectHashes.Clear();

            HashSet<StatusEffect> equipment;
            try
            {
                equipment =
                    EquipmentStatusEffects(player);
            }
            catch
            {
                return;
            }

            if (equipment == null ||
                equipment.Count == 0)
                return;

            foreach (StatusEffect effect in equipment)
            {
                if (effect != null)
                    EquipmentEffectHashes.Add(effect.NameHash());
            }

            if (EquipmentEffectHashes.Count == 0)
                return;

            for (int i = effects.Count - 1; i >= 0; i--)
            {
                StatusEffect effect = effects[i];
                if (effect != null &&
                    EquipmentEffectHashes.Contains(effect.NameHash()))
                {
                    effects.RemoveAt(i);
                }
            }
        }

        internal static Sprite GetForsakenUnavailableSprite(
            Sprite source)
        {
            if (source == null)
                return null;

            int id = source.GetInstanceID();

            if (ForsakenUnavailableSprites.TryGetValue(
                    id,
                    out Sprite cached) &&
                cached != null)
            {
                return cached;
            }

            Sprite generated =
                CreateForsakenUnavailableSprite(source);

            if (generated != null)
                ForsakenUnavailableSprites[id] = generated;

            return generated ?? source;
        }

        private static Sprite CreateForsakenUnavailableSprite(
            Sprite source)
        {
            Texture2D sourceTexture =
                source != null
                    ? source.texture
                    : null;

            if (sourceTexture == null)
                return null;

            RenderTexture temporary = null;
            RenderTexture previous = null;
            Texture2D texture = null;

            try
            {
                Rect sourceRect =
                    source.textureRect;

                int width =
                    Mathf.Max(
                        1,
                        Mathf.RoundToInt(sourceRect.width));

                int height =
                    Mathf.Max(
                        1,
                        Mathf.RoundToInt(sourceRect.height));

                temporary =
                    RenderTexture.GetTemporary(
                        width,
                        height,
                        0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.Default);

                Vector2 scale =
                    new Vector2(
                        sourceRect.width /
                        sourceTexture.width,
                        sourceRect.height /
                        sourceTexture.height);

                Vector2 offset =
                    new Vector2(
                        sourceRect.x /
                        sourceTexture.width,
                        sourceRect.y /
                        sourceTexture.height);

                Graphics.Blit(
                    sourceTexture,
                    temporary,
                    scale,
                    offset);

                previous =
                    RenderTexture.active;

                RenderTexture.active =
                    temporary;

                texture =
                    new Texture2D(
                        width,
                        height,
                        TextureFormat.RGBA32,
                        false);

                texture.name =
                    source.name +
                    "_SmoothUI_Unavailable";

                texture.ReadPixels(
                    new Rect(
                        0f,
                        0f,
                        width,
                        height),
                    0,
                    0,
                    false);

                Color32[] pixels =
                    texture.GetPixels32();

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel =
                        pixels[i];

                    int luminance =
                        (54 * pixel.r +
                         183 * pixel.g +
                         19 * pixel.b) >> 8;

                    byte dark =
                        (byte)Mathf.Clamp(
                            Mathf.RoundToInt(
                                luminance * 0.58f),
                            0,
                            255);

                    pixels[i] =
                        new Color32(
                            dark,
                            dark,
                            dark,
                            pixel.a);
                }

                texture.SetPixels32(pixels);
                texture.Apply(
                    false,
                    true);

                Vector2 pivot =
                    new Vector2(
                        source.rect.width > 0f
                            ? source.pivot.x /
                              source.rect.width
                            : 0.5f,
                        source.rect.height > 0f
                            ? source.pivot.y /
                              source.rect.height
                            : 0.5f);

                Sprite generated =
                    Sprite.Create(
                        texture,
                        new Rect(
                            0f,
                            0f,
                            width,
                            height),
                        pivot,
                        source.pixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect,
                        source.border);

                generated.name =
                    source.name +
                    "_SmoothUI_Unavailable";

                ForsakenUnavailableTextures.Add(texture);
                return generated;
            }
            catch
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);

                return null;
            }
            finally
            {
                RenderTexture.active =
                    previous;

                if (temporary != null)
                    RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void ClearForsakenUnavailableSprites()
        {
            foreach (Sprite sprite in
                     ForsakenUnavailableSprites.Values)
            {
                if (sprite != null)
                    UnityEngine.Object.Destroy(sprite);
            }

            ForsakenUnavailableSprites.Clear();

            for (int i = 0;
                 i < ForsakenUnavailableTextures.Count;
                 i++)
            {
                Texture2D texture =
                    ForsakenUnavailableTextures[i];

                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }

            ForsakenUnavailableTextures.Clear();
        }

        internal sealed class CanvasFadeGate
        {
            private readonly GameObject _root;
            private readonly CanvasGroup _group;
            private readonly float _baseAlpha;
            private readonly bool _baseInteractable;
            private readonly bool _baseBlocksRaycasts;
            private readonly bool _touchOnlyRaycasts;
            private readonly bool _preserveInteractable;

            private float _alpha;
            private float _hideTimer;

            internal GameObject Root => _root;

            internal bool IsAlive =>
                _root != null &&
                _group != null;

            internal CanvasFadeGate(
                GameObject root,
                bool startHidden,
                bool touchOnlyRaycasts = false,
                bool preserveInteractable = false)
            {
                _root = root;
                _touchOnlyRaycasts = touchOnlyRaycasts;
                _preserveInteractable = preserveInteractable;

                if (_root == null)
                    return;

                _group = _root.GetComponent<CanvasGroup>();
                if (_group == null)
                    _group = _root.AddComponent<CanvasGroup>();

                _baseAlpha = _group.alpha;
                _baseInteractable = _group.interactable;
                _baseBlocksRaycasts = _group.blocksRaycasts;

                _alpha = startHidden ? 0f : _baseAlpha;
                _hideTimer = startHidden ? 999f : 0f;

                Apply();
            }

            internal void Update(
                bool wanted,
                float dt,
                float hideDelay = 0f,
                float showSeconds = HudVisibilityReworkSystem.FadeSeconds,
                float hideSeconds = HudVisibilityReworkSystem.FadeSeconds)
            {
                if (!IsAlive)
                    return;

                if (wanted)
                    _hideTimer = 0f;
                else
                    _hideTimer += dt;

                bool showing =
                    wanted || _hideTimer < Mathf.Max(0f, hideDelay);

                float target =
                    showing
                        ? _baseAlpha
                        : 0f;

                float duration =
                    showing
                        ? Mathf.Max(0.01f, showSeconds)
                        : Mathf.Max(0.01f, hideSeconds);

                float speed =
                    Mathf.Max(0.01f, _baseAlpha) /
                    duration;

                _alpha =
                    duration <= 0.01f
                        ? target
                        : Mathf.MoveTowards(
                            _alpha,
                            target,
                            speed * dt);

                Apply();
            }

            internal void Restore()
            {
                if (!IsAlive)
                    return;

                _hideTimer = 0f;
                _alpha = _baseAlpha;
                Apply();
            }

            private void Apply()
            {
                if (!IsAlive)
                    return;

                if (Mathf.Abs(_group.alpha - _alpha) > 0.0005f)
                    _group.alpha = _alpha;

                bool visible =
                    _alpha > 0.02f;

                bool interactable =
                    _preserveInteractable
                        ? _baseInteractable
                        : visible &&
                          _baseInteractable;

                if (_group.interactable != interactable)
                    _group.interactable = interactable;

                bool blocksRaycasts;

                if (!visible)
                {
                    blocksRaycasts = false;
                }
                else if (_touchOnlyRaycasts)
                {
                    // The vanilla hotbar's pointer action is touch-oriented.
                    // Keeping desktop raycasts off prevents Unity's button
                    // Highlighted state from flashing when a hidden bar returns.
                    blocksRaycasts =
                        _baseBlocksRaycasts &&
                        ZInput.IsTouchActive();
                }
                else
                {
                    blocksRaycasts =
                        _baseBlocksRaycasts;
                }

                if (_group.blocksRaycasts != blocksRaycasts)
                    _group.blocksRaycasts = blocksRaycasts;
            }
        }

        internal sealed class GraphicFadeSet
        {
            private sealed class Target
            {
                internal Graphic Graphic;
                internal float BaseRendererAlpha;
                internal bool BaseRaycastTarget;
            }

            private readonly List<Target> _targets =
                new List<Target>();

            private float _factor;
            private float _hideTimer;

            internal bool IsEmpty =>
                _targets.Count == 0;

            internal bool IsFullyVisible => _factor >= 0.999f;
            internal bool IsFullyHidden => _factor <= 0.001f;

            internal GraphicFadeSet(
                IEnumerable<Graphic> graphics,
                bool startHidden)
            {
                HashSet<Graphic> seen =
                    new HashSet<Graphic>();

                if (graphics != null)
                {
                    foreach (Graphic graphic in graphics)
                    {
                        if (graphic == null ||
                            !seen.Add(graphic))
                            continue;

                        _targets.Add(
                            new Target
                            {
                                Graphic = graphic,
                                BaseRendererAlpha =
                                    graphic.canvasRenderer != null
                                        ? graphic.canvasRenderer.GetAlpha()
                                        : 1f,
                                BaseRaycastTarget =
                                    graphic.raycastTarget
                            });
                    }
                }

                _factor = startHidden ? 0f : 1f;
                _hideTimer = startHidden ? 999f : 0f;
                Apply();
            }

            internal void Update(
                bool wanted,
                float dt,
                float hideDelay = 0f,
                float fadeSeconds = HudVisibilityReworkSystem.FadeSeconds)
            {
                if (_targets.Count == 0)
                    return;

                if (wanted)
                    _hideTimer = 0f;
                else
                    _hideTimer += dt;

                float target =
                    wanted || _hideTimer < Mathf.Max(0f, hideDelay)
                        ? 1f
                        : 0f;

                _factor =
                    Mathf.MoveTowards(
                        _factor,
                        target,
                        fadeSeconds <= 0f ? 1f : dt / fadeSeconds);

                Apply();
            }

            internal void Restore()
            {
                if (_targets.Count == 0)
                    return;

                _hideTimer = 0f;
                _factor = 1f;
                Apply();
            }

            private void Apply()
            {
                bool visible =
                    _factor > 0.02f;

                for (int i = 0; i < _targets.Count; i++)
                {
                    Target target = _targets[i];
                    Graphic graphic = target.Graphic;

                    if (graphic == null)
                        continue;

                    if (graphic.canvasRenderer != null)
                    {
                        float desiredAlpha =
                            target.BaseRendererAlpha *
                            _factor;

                        if (Mathf.Abs(
                                graphic.canvasRenderer.GetAlpha() -
                                desiredAlpha) > 0.0005f)
                        {
                            graphic.canvasRenderer.SetAlpha(
                                desiredAlpha);
                        }
                    }

                    bool raycastTarget =
                        visible &&
                        target.BaseRaycastTarget;

                    if (graphic.raycastTarget != raycastTarget)
                        graphic.raycastTarget = raycastTarget;
                }
            }
        }
    }

    internal sealed class SmoothUIHudVisibilityReworkDriver :
        MonoBehaviour
    {
        private Hud _hud;

        private HotkeyBar _hotbar;
        private HotkeyBar _quickbar;

        private HudVisibilityReworkSystem.CanvasFadeGate _hotbarGate;
        private HudVisibilityReworkSystem.CanvasFadeGate _quickbarGate;


        private HudVisibilityReworkSystem.CanvasFadeGate _staminaGate;
        private HudVisibilityReworkSystem.CanvasFadeGate _eitrGate;
        private HudVisibilityReworkSystem.CanvasFadeGate _adrenalineGate;
        private HudVisibilityReworkSystem.CanvasFadeGate _mluiWeightGate;
        private HudVisibilityReworkSystem.CanvasFadeGate _mluiSlotsGate;

        private HudVisibilityReworkSystem.GraphicFadeSet _healthSet;

        // The slow and base-health fills can be yellow beneath the
        // player's red health fill. Match their RGB only while
        // disappearing, then restore their own original colors.
        private readonly List<Image> _healthFadeUnderfills =
            new List<Image>();
        private readonly List<Color> _healthFadeOriginalColors =
            new List<Color>();
        private readonly List<Image> _hiddenHealthBackings =
            new List<Image>();
        private readonly List<bool> _backingOriginalEnabled =
            new List<bool>();
        private HudVisibilityReworkSystem.GraphicFadeSet _foodSet;
        private HudVisibilityReworkSystem.GraphicFadeSet _forsakenSet;

        private StatusEffect _forsakenCachedPower;
        private Sprite _forsakenOriginalSprite;
        private Sprite _forsakenUnavailableSprite;

        private float _nextCompatibilityBind;

        private readonly Dictionary<Player.Food, bool>
            _foodReplaceableState =
                new Dictionary<Player.Food, bool>();

        private readonly List<Player.Food>
            _staleFoods =
                new List<Player.Food>();

        private float _foodReplaceableRevealUntil =
            -999f;

        private float _hotbarRevealUntil =
            -999f;

        private bool _hotbarActionWasActive;
        private float _lastForsakenCooldown = -1f;
        private float _forsakenOnUseUntil = -999f;

        internal void Initialize(Hud hud)
        {
            _hud = hud;
            HudVisibilityReworkSystem.Register(this);

            BindVanillaTargets();
            TryBindCompatibility(force: true);
        }

        private void BindVanillaTargets()
        {
            if (_hud == null)
                return;

            Transform hudRoot =
                _hud.m_rootObject != null
                    ? _hud.m_rootObject.transform
                    : null;

            Transform hotbarTransform =
                hudRoot != null
                    ? hudRoot.Find("HotKeyBar")
                    : null;

            if (hotbarTransform != null)
            {
                _hotbar =
                    hotbarTransform.GetComponent<HotkeyBar>();

                _hotbarGate =
                    new HudVisibilityReworkSystem.CanvasFadeGate(
                        hotbarTransform.gameObject,
                        WiresSmoothUIPlugin.DynamicHotbar.Value,
                        touchOnlyRaycasts: true,
                        preserveInteractable: true);

            }

            _staminaGate =
                new HudVisibilityReworkSystem.CanvasFadeGate(
                    _hud.m_staminaBar2Root != null
                        ? _hud.m_staminaBar2Root.gameObject
                        : null,
                    WiresSmoothUIPlugin.DynamicStamina.Value);

            _eitrGate =
                new HudVisibilityReworkSystem.CanvasFadeGate(
                    _hud.m_eitrBarRoot != null
                        ? _hud.m_eitrBarRoot.gameObject
                        : null,
                    WiresSmoothUIPlugin.DynamicEitr.Value);

            _adrenalineGate =
                new HudVisibilityReworkSystem.CanvasFadeGate(
                    _hud.m_adrenalineBarRoot != null
                        ? _hud.m_adrenalineBarRoot.gameObject
                        : null,
                    WiresSmoothUIPlugin.DynamicAdrenaline.Value);

            HashSet<Graphic> foodGraphics =
                CollectFoodGraphics();

            HashSet<Graphic> healthGraphics =
                CollectHealthGraphics(foodGraphics);

            HashSet<Graphic> forsakenGraphics =
                new HashSet<Graphic>();

            if (_hud.m_gpRoot != null)
            {
                AddGraphicsUnder(
                    _hud.m_gpRoot,
                    forsakenGraphics);
            }

            _foodSet =
                new HudVisibilityReworkSystem.GraphicFadeSet(
                    foodGraphics,
                    WiresSmoothUIPlugin.DynamicFood.Value);

            _healthSet =
                new HudVisibilityReworkSystem.GraphicFadeSet(
                    healthGraphics,
                    WiresSmoothUIPlugin.DynamicHealth.Value);

            _forsakenSet =
                new HudVisibilityReworkSystem.GraphicFadeSet(
                    forsakenGraphics,
                    WiresSmoothUIPlugin.DynamicForsakenPower.Value);
        }

        private HashSet<Graphic> CollectFoodGraphics()
        {
            HashSet<Graphic> result =
                new HashSet<Graphic>();

            if (_hud == null)
                return result;

            List<Transform> seeds =
                new List<Transform>(16);

            AddSeed(seeds, _hud.m_foodBarRoot);
            AddSeed(seeds, _hud.m_foodBaseBar);

            if (_hud.m_foodIcon != null)
                AddSeed(seeds, _hud.m_foodIcon.transform);

            if (_hud.m_foodText != null)
                AddSeed(seeds, _hud.m_foodText.transform);

            if (_hud.m_foodBars != null)
            {
                for (int i = 0; i < _hud.m_foodBars.Length; i++)
                {
                    if (_hud.m_foodBars[i] != null)
                        AddSeed(
                            seeds,
                            _hud.m_foodBars[i].transform);
                }
            }

            if (_hud.m_foodIcons != null)
            {
                for (int i = 0; i < _hud.m_foodIcons.Length; i++)
                {
                    if (_hud.m_foodIcons[i] != null)
                        AddSeed(
                            seeds,
                            _hud.m_foodIcons[i].transform);
                }
            }

            if (_hud.m_foodTime != null)
            {
                for (int i = 0; i < _hud.m_foodTime.Length; i++)
                {
                    if (_hud.m_foodTime[i] != null)
                        AddSeed(
                            seeds,
                            _hud.m_foodTime[i].transform);
                }
            }

            for (int i = 0; i < seeds.Count; i++)
            {
                Transform seed = seeds[i];
                AddGraphicsUnder(seed, result);

                Transform branch =
                    FindHighestFoodOnlyBranch(seed);

                if (branch != null)
                    AddGraphicsUnder(branch, result);
            }

            if (_hud.m_healthPanel != null)
            {
                Graphic[] panelGraphics =
                    _hud.m_healthPanel
                        .GetComponentsInChildren<Graphic>(true);

                for (int i = 0; i < panelGraphics.Length; i++)
                {
                    Graphic graphic = panelGraphics[i];
                    if (graphic != null &&
                        LooksFoodRelated(graphic))
                    {
                        result.Add(graphic);
                    }
                }
            }

            return result;
        }

        private HashSet<Graphic> CollectHealthGraphics(
            HashSet<Graphic> foodGraphics)
        {
            HashSet<Graphic> result =
                new HashSet<Graphic>();

            if (_hud == null)
                return result;

            if (_hud.m_healthBarRoot != null)
            {
                AddGraphicsUnder(
                    _hud.m_healthBarRoot,
                    result);

                Transform healthBranch =
                    FindHighestHealthOnlyBranch(
                        _hud.m_healthBarRoot);

                if (healthBranch != null)
                {
                    AddGraphicsUnder(
                        healthBranch,
                        result);
                }
            }

            if (_hud.m_healthText != null)
                result.Add(_hud.m_healthText);

            if (_hud.m_healthPanel != null)
            {
                Graphic[] panelGraphics =
                    _hud.m_healthPanel
                        .GetComponentsInChildren<Graphic>(true);

                for (int i = 0; i < panelGraphics.Length; i++)
                {
                    Graphic graphic = panelGraphics[i];
                    if (graphic == null ||
                        graphic.transform == _hud.m_healthPanel ||
                        (foodGraphics != null &&
                         foodGraphics.Contains(graphic)))
                        continue;

                    if (LooksHealthRelated(graphic))
                        result.Add(graphic);
                }
            }

            return result;
        }

        private Transform FindHighestFoodOnlyBranch(
            Transform seed)
        {
            if (seed == null)
                return null;

            Transform panel =
                _hud != null
                    ? _hud.m_healthPanel
                    : null;

            Transform healthRoot =
                _hud != null
                    ? _hud.m_healthBarRoot
                    : null;

            Transform current = seed;

            while (current.parent != null &&
                   current.parent != panel)
            {
                Transform parent =
                    current.parent;

                if (healthRoot != null &&
                    healthRoot.IsChildOf(parent))
                    break;

                current = parent;
            }

            return current;
        }

        private Transform FindHighestHealthOnlyBranch(
            Transform seed)
        {
            if (seed == null)
                return null;

            Transform panel =
                _hud != null
                    ? _hud.m_healthPanel
                    : null;

            Transform foodRoot =
                _hud != null
                    ? _hud.m_foodBarRoot
                    : null;

            Transform foodIcon =
                _hud != null && _hud.m_foodIcon != null
                    ? _hud.m_foodIcon.transform
                    : null;

            Transform current = seed;

            while (current.parent != null &&
                   current.parent != panel)
            {
                Transform parent =
                    current.parent;

                bool containsFood =
                    (foodRoot != null &&
                     foodRoot.IsChildOf(parent)) ||
                    (foodIcon != null &&
                     foodIcon.IsChildOf(parent));

                if (containsFood)
                    break;

                current = parent;
            }

            return current;
        }

        private static void AddSeed(
            List<Transform> seeds,
            Transform transform)
        {
            if (seeds == null ||
                transform == null)
                return;

            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i] == transform)
                    return;
            }

            seeds.Add(transform);
        }

        private static void AddGraphicsUnder(
            Transform root,
            HashSet<Graphic> output)
        {
            if (root == null ||
                output == null)
                return;

            Graphic[] graphics =
                root.GetComponentsInChildren<Graphic>(true);

            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic != null)
                    output.Add(graphic);
            }
        }

        private static bool LooksFoodRelated(
            Graphic graphic)
        {
            if (graphic == null)
                return false;

            if (NameContains(
                    graphic.name,
                    "food",
                    "fork",
                    "hunger"))
                return true;

            Image image = graphic as Image;
            if (image != null &&
                image.sprite != null &&
                NameContains(
                    image.sprite.name,
                    "food",
                    "fork",
                    "hunger"))
                return true;

            Transform current = graphic.transform.parent;
            int depth = 0;

            while (current != null && depth < 4)
            {
                if (NameContains(
                        current.name,
                        "food",
                        "fork",
                        "hunger"))
                    return true;

                current = current.parent;
                depth++;
            }

            return false;
        }

        private static bool LooksHealthRelated(
            Graphic graphic)
        {
            if (graphic == null)
                return false;

            if (NameContains(
                    graphic.name,
                    "health",
                    "heart",
                    "hp"))
                return true;

            Image image = graphic as Image;
            if (image != null &&
                image.sprite != null &&
                NameContains(
                    image.sprite.name,
                    "health",
                    "heart",
                    "hp"))
                return true;

            Transform current = graphic.transform.parent;
            int depth = 0;

            while (current != null && depth < 4)
            {
                if (NameContains(
                        current.name,
                        "health",
                        "heart",
                        "hp"))
                    return true;

                current = current.parent;
                depth++;
            }

            return false;
        }

        private static bool NameContains(
            string value,
            string a,
            string b,
            string c)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.IndexOf(
                       a,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(
                       b,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(
                       c,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object TryGetPluginStaticMember(
            string pluginGuid,
            string typeName,
            string memberName)
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(
                        pluginGuid,
                        out var pluginInfo) ||
                    pluginInfo == null ||
                    pluginInfo.Instance == null)
                    return null;

                Assembly assembly =
                    pluginInfo.Instance.GetType().Assembly;

                Type type =
                    assembly.GetType(
                        typeName,
                        false);

                if (type == null)
                    return null;

                const BindingFlags flags =
                    BindingFlags.Public |
                    BindingFlags.Static;

                FieldInfo field =
                    type.GetField(
                        memberName,
                        flags);

                if (field != null)
                    return field.GetValue(null);

                PropertyInfo property =
                    type.GetProperty(
                        memberName,
                        flags);

                if (property != null)
                    return property.GetValue(
                        null,
                        null);
            }
            catch
            {
                // Third-party integrations are soft dependencies. If a mod
                // changes its internals, leave that compatibility target
                // unbound instead of breaking Smooth UI or the HUD.
            }

            return null;
        }

        private static GameObject TryGetGameObject(
            object value)
        {
            if (value is GameObject gameObject)
                return gameObject;

            if (value is Component component)
                return component.gameObject;

            return null;
        }

        private void TryBindCompatibility(bool force)
        {
            if (_hud == null ||
                _hud.m_rootObject == null)
                return;

            if (!force &&
                Time.unscaledTime < _nextCompatibilityBind)
                return;

            _nextCompatibilityBind =
                Time.unscaledTime + 1f;

            Transform root =
                _hud.m_rootObject.transform;

            // AzuEPI's public HotkeyBars property does not point at the same
            // visible quick-access root that Smooth UI historically faded.
            // Restore the proven HUD binding here rather than targeting the
            // wrong object through reflection.
            if (_quickbar == null ||
                _quickbarGate == null ||
                !_quickbarGate.IsAlive)
            {
                Transform quick =
                    root.Find("AzuEPI_QuickAccessBar");

                if (quick != null)
                {
                    _quickbar =
                        quick.GetComponent<HotkeyBar>();

                    _quickbarGate =
                        new HudVisibilityReworkSystem.CanvasFadeGate(
                            quick.gameObject,
                            WiresSmoothUIPlugin.DynamicHotbar.Value,
                            preserveInteractable: true);

                }
            }

            if (_mluiWeightGate == null ||
                !_mluiWeightGate.IsAlive)
            {
                GameObject weight =
                    TryGetGameObject(
                        TryGetPluginStaticMember(
                            "shudnal.MyLittleUI",
                            "MyLittleUI.InventoryPanel",
                            "weight"));

                if (weight != null)
                {
                    _mluiWeightGate =
                        new HudVisibilityReworkSystem.CanvasFadeGate(
                            weight,
                            WiresSmoothUIPlugin.DynamicMluiWeight.Value);
                }
            }

            if (_mluiSlotsGate == null ||
                !_mluiSlotsGate.IsAlive)
            {
                GameObject slots =
                    TryGetGameObject(
                        TryGetPluginStaticMember(
                            "shudnal.MyLittleUI",
                            "MyLittleUI.InventoryPanel",
                            "slots"));

                if (slots != null)
                {
                    _mluiSlotsGate =
                        new HudVisibilityReworkSystem.CanvasFadeGate(
                            slots,
                            WiresSmoothUIPlugin.DynamicMluiSlots.Value);
                }
            }
        }

        private void LateUpdate()
        {
            if (_hud == null)
                return;

            TryBindCompatibility(force: false);

            if (!WiresSmoothUIPlugin.HudEnabled.Value)
            {
                RestoreDynamicOnly();
                Player resourcePlayer = Player.m_localPlayer;
                if (WiresSmoothUIPlugin.Enabled.Value && resourcePlayer != null)
                    UpdateResourceBars(resourcePlayer, MapStatusVisibilitySystem.ActivatorHeld,
                        Mathf.Min(Time.unscaledDeltaTime, 0.05f));
                else
                    RestoreResourceBars();
                return;
            }

            Player player =
                Player.m_localPlayer;

            if (player == null)
            {
                RestoreEverything();
                return;
            }

            float dt =
                Mathf.Min(
                    Time.unscaledDeltaTime,
                    0.05f);

            bool inventoryOpen =
                InventoryGui.IsVisible();

            bool combat =
                HudVisibilityReworkSystem
                    .IsCombatRelevant(player);

            bool activator =
                MapStatusVisibilitySystem
                    .ActivatorHeld;

            UpdateHotbars(
                player,
                combat,
                activator,
                dt);

            UpdateHealth(
                player,
                combat,
                inventoryOpen,
                activator,
                dt);

            UpdateFood(
                player,
                combat,
                inventoryOpen,
                activator,
                dt);

            if (WiresSmoothUIPlugin.Enabled.Value)
                UpdateResourceBars(player, activator, dt);
            else
                RestoreResourceBars();

            UpdateForsakenPower(
                player,
                combat,
                inventoryOpen,
                activator,
                dt);

            UpdateMlui(
                inventoryOpen,
                activator,
                dt);
        }

        private void UpdateHotbars(
            Player player,
            bool combat,
            bool activator,
            float dt)
        {
            if (!WiresSmoothUIPlugin.DynamicHotbar.Value)
            {
                _hotbarGate?.Restore();
                _quickbarGate?.Restore();
                _hotbarActionWasActive = false;
                _hotbarRevealUntil = -999f;
                return;
            }

            if (WiresSmoothUIPlugin.HotbarCycle.Value &&
                ZInput.IsGamepadActive() &&
                !ZInput.GetButton("JoyAltKeys") &&
                !InventoryGui.IsVisible() &&
                !Menu.IsVisible() &&
                !Minimap.IsOpen() &&
                !Hud.IsPieceSelectionVisible() &&
                !StoreGui.IsVisible() &&
                !Console.IsVisible() &&
                !Hud.InRadial() &&
                (Chat.instance == null || !Chat.instance.HasFocus()) &&
                (ZInput.GetButtonDown("JoyHotbarLeft") ||
                 ZInput.GetButtonDown("JoyHotbarRight")))
            {
                _hotbarRevealUntil =
                    Mathf.Max(_hotbarRevealUntil, Time.unscaledTime + 1f);
            }

            bool equipActionActive =
                WiresSmoothUIPlugin
                    .HotbarEquipActions.Value &&
                HudVisibilityReworkSystem
                    .ActiveEquipActionItem(player) != null;

            if (_hotbarActionWasActive &&
                !equipActionActive)
            {
                _hotbarRevealUntil =
                    Mathf.Max(
                        _hotbarRevealUntil,
                        Time.unscaledTime +
                        HudVisibilityReworkSystem
                            .HotbarPostActionHoldSeconds);
            }

            _hotbarActionWasActive =
                equipActionActive;

            bool heldItem =
                WiresSmoothUIPlugin
                    .HotbarHeldItem.Value &&
                (player.RightItem != null ||
                 player.LeftItem != null);

            bool wanted =
                (WiresSmoothUIPlugin
                    .HotbarCombat.Value &&
                 combat) ||
                (WiresSmoothUIPlugin
                    .HotbarActivator.Value &&
                 activator) ||
                equipActionActive ||
                heldItem ||
                Time.unscaledTime <
                    _hotbarRevealUntil;

            // Both bars share the configured hotbar visibility triggers.
            _hotbarGate?.Update(
                wanted,
                dt,
                0f,
                WiresSmoothUIPlugin.HotbarFadeSeconds.Value,
                WiresSmoothUIPlugin.HotbarFadeSeconds.Value);

            if (WiresSmoothUIPlugin.DynamicAzuQuickbar.Value)
                _quickbarGate?.Update(wanted, dt, 0f,
                    WiresSmoothUIPlugin.HotbarFadeSeconds.Value,
                    WiresSmoothUIPlugin.HotbarFadeSeconds.Value);
            else
                _quickbarGate?.Restore();
        }

        private static bool FadeRectsOverlap(
            RectTransform a, RectTransform b)
        {
            if (a == null || b == null)
                return false;

            Vector3[] ac = new Vector3[4];
            Vector3[] bc = new Vector3[4];
            a.GetWorldCorners(ac);
            b.GetWorldCorners(bc);
            return ac[0].x < bc[2].x && ac[2].x > bc[0].x &&
                   ac[0].y < bc[2].y && ac[2].y > bc[0].y;
        }

        private void RestoreHealthUnderfillColors()
        {
            for (int i = 0; i < _healthFadeUnderfills.Count; i++)
                if (_healthFadeUnderfills[i] != null)
                    _healthFadeUnderfills[i].color =
                        _healthFadeOriginalColors[i];

            _healthFadeUnderfills.Clear();
            _healthFadeOriginalColors.Clear();
        }

        private void TintHealthUnderfill(Image underfill, Color fast)
        {
            if (underfill == null)
                return;

            int index = _healthFadeUnderfills.IndexOf(underfill);
            if (index < 0)
            {
                _healthFadeUnderfills.Add(underfill);
                _healthFadeOriginalColors.Add(underfill.color);
                index = _healthFadeOriginalColors.Count - 1;
            }

            Color original = _healthFadeOriginalColors[index];
            Color desired = new Color(fast.r, fast.g, fast.b, original.a);
            if (underfill.color != desired)
                underfill.color = desired;
        }

        private void UpdateHealthUnderfillColor(bool wanted)
        {
            if (wanted || _hud == null ||
                _hud.m_healthBarFast == null)
            {
                RestoreHealthUnderfillColors();
                return;
            }

            Color fast = _hud.m_healthBarFast.GetColor();
            if (_hud.m_healthBarSlow != null &&
                _hud.m_healthBarSlow.m_bar != null)
                TintHealthUnderfill(
                    _hud.m_healthBarSlow.m_bar.GetComponent<Image>(),
                    fast);

            // The food base fill sometimes occupies the same visual
            // space as the player's HP fill. Don't alter unrelated
            // food widgets or their independent fade.
            if (_hud.m_foodBaseBar != null &&
                FadeRectsOverlap(_hud.m_healthBarRoot,
                    _hud.m_foodBaseBar))
                TintHealthUnderfill(
                    _hud.m_foodBaseBar.GetComponent<Image>(),
                    fast);
        }

        private void RestoreHealthBackingImages()
        {
            for (int i = 0; i < _hiddenHealthBackings.Count; i++)
            {
                Image image = _hiddenHealthBackings[i];
                if (image != null)
                    image.enabled = _backingOriginalEnabled[i];
            }
            _hiddenHealthBackings.Clear();
            _backingOriginalEnabled.Clear();
        }

        private void HideHealthBackingImage(Image image)
        {
            if (image == null)
                return;

            if (!_hiddenHealthBackings.Contains(image))
            {
                _hiddenHealthBackings.Add(image);
                _backingOriginalEnabled.Add(image.enabled);
            }

            if (image.enabled)
                image.enabled = false;
        }

        private void KeepHealthBackingHiddenDuringFade(bool fullyVisible)
        {
            if (fullyVisible || _hud == null)
            {
                RestoreHealthBackingImages();
                return;
            }

            // Transparent red over the native yellow slow/base fills
            // blends into yellow. Prevent backing layers from rendering
            // during either half of the fade, not only recoloring them.
            if (_hud.m_healthBarSlow != null &&
                _hud.m_healthBarSlow.m_bar != null)
                HideHealthBackingImage(
                    _hud.m_healthBarSlow.m_bar.GetComponent<Image>());
            if (_hud.m_foodBaseBar != null)
                HideHealthBackingImage(
                    _hud.m_foodBaseBar.GetComponent<Image>());
        }

        private void UpdateHealth(
            Player player,
            bool combat,
            bool inventoryOpen,
            bool activator,
            float dt)
        {
            if (_healthSet == null)
                return;

            if (!WiresSmoothUIPlugin.DynamicHealth.Value)
            {
                RestoreHealthBackingImages();
                RestoreHealthUnderfillColors();
                _healthSet.Restore();
                return;
            }

            float health =
                player.GetHealth();

            float maxHealth =
                Mathf.Max(
                    1f,
                    player.GetMaxHealth());

            bool wanted =
                (WiresSmoothUIPlugin.HealthCombat.Value &&
                 combat) ||
                (WiresSmoothUIPlugin.HealthInventory.Value &&
                 inventoryOpen) ||
                (WiresSmoothUIPlugin.HealthActivator.Value &&
                 activator) ||
                (health < maxHealth *
                    (WiresSmoothUIPlugin.HealthThresholdChoice.Value == "100.0%" ? 1f : 0.951f));

            UpdateHealthUnderfillColor(wanted);
            _healthSet.Update(
                wanted, dt, 0f, WiresSmoothUIPlugin.HealthFadeSeconds.Value);
            KeepHealthBackingHiddenDuringFade(
                (wanted && _healthSet.IsFullyVisible) ||
                (!wanted && _healthSet.IsFullyHidden));
        }

        private void UpdateFood(
            Player player,
            bool combat,
            bool inventoryOpen,
            bool activator,
            float dt)
        {
            if (_foodSet == null)
                return;

            if (!WiresSmoothUIPlugin.DynamicFood.Value)
            {
                _foodSet.Restore();
                return;
            }

            List<Player.Food> foods =
                player.GetFoods();

            bool becameReplaceable = false;
            bool anyReplaceable = false;
            bool expiryFlash = false;

            if (foods != null)
            {
                for (int i = 0; i < foods.Count; i++)
                {
                    Player.Food food =
                        foods[i];

                    if (food == null ||
                        food.m_item == null)
                        continue;

                    bool replaceable =
                        food.CanEatAgain();
                    if (replaceable) anyReplaceable = true;

                    if (_foodReplaceableState.TryGetValue(
                            food,
                            out bool wasReplaceable))
                    {
                        if (!wasReplaceable &&
                            replaceable)
                        {
                            becameReplaceable = true;
                        }

                        _foodReplaceableState[food] =
                            replaceable;
                    }
                    else
                    {
                        // Establish a baseline without firing a fake
                        // transition when loading into an existing meal.
                        _foodReplaceableState.Add(
                            food,
                            replaceable);
                    }

                    float remainingSeconds =
                        food.m_time /
                        Mathf.Max(
                            0.001f,
                            Game.m_foodRate);

                    // Match vanilla's flashing food timer exactly:
                    // Hud.UpdateFood starts pulsing below 60 seconds.
                    if (WiresSmoothUIPlugin
                            .FoodFinalMinute.Value &&
                        remainingSeconds > 0f &&
                        remainingSeconds <
                            HudVisibilityReworkSystem
                                .FoodExpiryFlashSeconds)
                    {
                        expiryFlash = true;
                    }
                }
            }

            _staleFoods.Clear();

            foreach (
                KeyValuePair<Player.Food, bool>
                    pair in _foodReplaceableState)
            {
                if (foods == null ||
                    !foods.Contains(pair.Key))
                {
                    _staleFoods.Add(pair.Key);
                }
            }

            for (int i = 0;
                 i < _staleFoods.Count;
                 i++)
            {
                _foodReplaceableState.Remove(
                    _staleFoods[i]);
            }

            if (becameReplaceable &&
                WiresSmoothUIPlugin
                    .FoodStateChanges.Value)
            {
                _foodReplaceableRevealUntil =
                    Time.unscaledTime +
                    HudVisibilityReworkSystem
                        .FoodReplaceableRevealSeconds;
            }

            bool replaceablePulse =
                Time.unscaledTime <
                _foodReplaceableRevealUntil;

            bool wanted =
                (WiresSmoothUIPlugin.FoodCombat.Value &&
                 combat) ||
                (WiresSmoothUIPlugin.FoodInventory.Value &&
                 inventoryOpen) ||
                (WiresSmoothUIPlugin.FoodActivator.Value &&
                 activator) ||
                expiryFlash ||
                (WiresSmoothUIPlugin.FoodReplaceable.Value && anyReplaceable) ||
                (WiresSmoothUIPlugin.FoodStateChanges.Value &&
                 (replaceablePulse ||
                  HudVisibilityReworkSystem
                    .FoodRecentlyEaten));

            _foodSet.Update(
                wanted, dt, 0f, WiresSmoothUIPlugin.FoodFadeSeconds.Value);
        }

        private void UpdateResourceBars(
            Player player,
            bool activator,
            float dt)
        {
            if (_staminaGate != null)
            {
                if (WiresSmoothUIPlugin.DynamicStamina.Value)
                {
                    float current =
                        player.GetStamina();

                    float maximum =
                        Mathf.Max(
                            0f,
                            player.GetMaxStamina());

                    bool wanted =
                        maximum > 0.001f &&
                        ((WiresSmoothUIPlugin.StaminaActivator.Value && activator) ||
                         current < maximum - 0.001f);

                    _staminaGate.Update(
                        wanted,
                        dt,
                        HudVisibilityReworkSystem
                            .ResourceHideDelay,
                        WiresSmoothUIPlugin.ResourceFadeSeconds.Value,
                        WiresSmoothUIPlugin.ResourceFadeSeconds.Value);
                }
                else
                {
                    _staminaGate.Restore();
                }
            }

            if (_eitrGate != null)
            {
                if (WiresSmoothUIPlugin.DynamicEitr.Value)
                {
                    float current =
                        player.GetEitr();

                    float maximum =
                        Mathf.Max(
                            0f,
                            player.GetMaxEitr());

                    bool wanted =
                        maximum > 0.001f &&
                        ((WiresSmoothUIPlugin.EitrActivator.Value && activator) ||
                         current < maximum - 0.001f);

                    _eitrGate.Update(
                        wanted,
                        dt,
                        HudVisibilityReworkSystem
                            .ResourceHideDelay,
                        WiresSmoothUIPlugin.ResourceFadeSeconds.Value,
                        WiresSmoothUIPlugin.ResourceFadeSeconds.Value);
                }
                else
                {
                    _eitrGate.Restore();
                }
            }

            if (_adrenalineGate != null)
            {
                if (WiresSmoothUIPlugin.DynamicAdrenaline.Value)
                {
                    float current =
                        Mathf.Max(
                            0f,
                            player.GetAdrenaline());

                    float maximum =
                        Mathf.Max(
                            0f,
                            player.GetMaxAdrenaline());

                    bool wanted =
                        maximum > 0.001f &&
                        ((WiresSmoothUIPlugin.AdrenalineActivator.Value && activator) ||
                         current > 0.001f);

                    _adrenalineGate.Update(
                        wanted, dt, 0f,
                        WiresSmoothUIPlugin.ResourceFadeSeconds.Value,
                        WiresSmoothUIPlugin.ResourceFadeSeconds.Value);
                }
                else
                {
                    _adrenalineGate.Restore();
                }
            }
        }

        private void UpdateForsakenPower(
            Player player,
            bool combat,
            bool inventoryOpen,
            bool activator,
            float dt)
        {
            if (_forsakenSet == null)
                return;

            if (!WiresSmoothUIPlugin.DynamicForsakenPower.Value)
            {
                _forsakenSet.Restore();
                return;
            }

            player.GetGuardianPowerHUD(
                out StatusEffect power,
                out float cooldown);

            bool hasPower =
                power != null;

            bool coolingDown =
                cooldown > 0f;
            if (_lastForsakenCooldown >= 0f &&
                cooldown > _lastForsakenCooldown + 2f &&
                cooldown > 5f)
                _forsakenOnUseUntil = Time.unscaledTime + 1.25f;
            _lastForsakenCooldown = cooldown;

            bool wanted =
                hasPower &&
                ((WiresSmoothUIPlugin.ForsakenCombat.Value &&
                  combat) ||
                 (WiresSmoothUIPlugin.ForsakenInventory.Value &&
                  inventoryOpen) ||
                 (WiresSmoothUIPlugin.ForsakenActivator.Value &&
                  activator) ||
                 (WiresSmoothUIPlugin.ForsakenOnUse.Value &&
                    Time.unscaledTime < _forsakenOnUseUntil) ||
                 (WiresSmoothUIPlugin.ForsakenCooldownEnding.Value &&
                  coolingDown &&
                  cooldown <=
                    HudVisibilityReworkSystem
                        .ForsakenPreviewSeconds));

            // Cache the expensive desaturated portrait as soon as the selected
            // power/icon changes, rather than creating or looking it up in the
            // per-frame path. Valheim rewrites m_gpIcon every HUD update, so
            // while cooling down we only re-apply the cached sprite after that.
            if (hasPower &&
                _hud.m_gpIcon != null)
            {
                Sprite original =
                    power.m_icon;

                if (!ReferenceEquals(
                        _forsakenCachedPower,
                        power) ||
                    _forsakenOriginalSprite != original)
                {
                    _forsakenCachedPower =
                        power;

                    _forsakenOriginalSprite =
                        original;

                    _forsakenUnavailableSprite =
                        HudVisibilityReworkSystem
                            .GetForsakenUnavailableSprite(
                                original);
                }

                Sprite desiredSprite =
                    coolingDown
                        ? _forsakenUnavailableSprite
                        : _forsakenOriginalSprite;

                if (desiredSprite != null &&
                    _hud.m_gpIcon.sprite != desiredSprite)
                {
                    _hud.m_gpIcon.sprite =
                        desiredSprite;
                }

                if (_hud.m_gpIcon.color != Color.white)
                {
                    _hud.m_gpIcon.color =
                        Color.white;
                }
            }
            else
            {
                _forsakenCachedPower = null;
                _forsakenOriginalSprite = null;
                _forsakenUnavailableSprite = null;
            }

            _forsakenSet.Update(
                wanted, dt, 0f, WiresSmoothUIPlugin.ForsakenFadeSeconds.Value);
        }

        private void UpdateMlui(
            bool inventoryOpen,
            bool activator,
            float dt)
        {
            if (_mluiWeightGate != null)
            {
                if (WiresSmoothUIPlugin
                    .DynamicMluiWeight.Value)
                {
                    bool wanted =
                        (WiresSmoothUIPlugin.MluiWeightInventory.Value &&
                         inventoryOpen) ||
                        (WiresSmoothUIPlugin.MluiWeightActivator.Value &&
                         activator);

                    _mluiWeightGate.Update(
                        wanted, dt, 0f,
                        WiresSmoothUIPlugin.MluiFadeSeconds.Value,
                        WiresSmoothUIPlugin.MluiFadeSeconds.Value);
                }
                else
                {
                    _mluiWeightGate.Restore();
                }
            }

            if (_mluiSlotsGate != null)
            {
                if (WiresSmoothUIPlugin
                    .DynamicMluiSlots.Value)
                {
                    bool wanted =
                        (WiresSmoothUIPlugin.MluiSlotsInventory.Value &&
                         inventoryOpen) ||
                        (WiresSmoothUIPlugin.MluiSlotsActivator.Value &&
                         activator);

                    _mluiSlotsGate.Update(
                        wanted, dt, 0f,
                        WiresSmoothUIPlugin.MluiFadeSeconds.Value,
                        WiresSmoothUIPlugin.MluiFadeSeconds.Value);
                }
                else
                {
                    _mluiSlotsGate.Restore();
                }
            }
        }

        private void RestoreResourceBars()
        {
            _staminaGate?.Restore();
            _eitrGate?.Restore();
            _adrenalineGate?.Restore();
        }

        private void RestoreDynamicOnly()
        {
            RestoreHealthUnderfillColors();
            _hotbarGate?.Restore();
            _quickbarGate?.Restore();
            _hotbarActionWasActive = false;
            _hotbarRevealUntil = -999f;
            _mluiWeightGate?.Restore();
            _mluiSlotsGate?.Restore();
            RestoreHealthBackingImages();
            _healthSet?.Restore();
            _foodSet?.Restore();
            _forsakenSet?.Restore();
        }

        internal void RestoreEverything()
        {
            RestoreHealthUnderfillColors();
            _hotbarGate?.Restore();
            _quickbarGate?.Restore();
            _hotbarActionWasActive = false;
            _hotbarRevealUntil = -999f;
            _staminaGate?.Restore();
            _eitrGate?.Restore();
            _adrenalineGate?.Restore();
            _mluiWeightGate?.Restore();
            _mluiSlotsGate?.Restore();

            RestoreHealthBackingImages();
            _healthSet?.Restore();
            _foodSet?.Restore();
            _forsakenSet?.Restore();
        }

        internal void NotifyHotbarEquipmentChanged()
        {
            if (!WiresSmoothUIPlugin.HotbarEquipActions.Value)
                return;

            float revealSeconds =
                _hotbarActionWasActive
                    ? HudVisibilityReworkSystem
                        .HotbarPostActionHoldSeconds
                    : HudVisibilityReworkSystem
                        .HotbarInstantEquipRevealSeconds;

            _hotbarRevealUntil =
                Mathf.Max(
                    _hotbarRevealUntil,
                    Time.unscaledTime +
                    revealSeconds);
        }

        private void OnDestroy()
        {
            RestoreEverything();
            HudVisibilityReworkSystem.Unregister(this);
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
    internal static class SmoothUIFoodRecentRevealPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Player __instance,
            bool __result)
        {
            if (__result)
            {
                HudVisibilityReworkSystem
                    .NotifyFoodEaten(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    [HarmonyBefore("shudnal.MyLittleUI")]
    internal static class SmoothUIStatusEffectFilterPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            List<StatusEffect> statusEffects)
        {
            HudVisibilityReworkSystem
                .FilterPermanentEquipmentEffects(
                    statusEffects);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateStamina")]
    internal static class SmoothUIStaminaFadePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (!WiresSmoothUIPlugin.HudEnabled.Value ||
                !WiresSmoothUIPlugin.DynamicStamina.Value ||
                __instance == null ||
                __instance.m_staminaAnimator == null)
                return;

            __instance.m_staminaAnimator.SetBool(
                "Visible",
                true);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateEitr")]
    internal static class SmoothUIEitrFadePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (!WiresSmoothUIPlugin.HudEnabled.Value ||
                !WiresSmoothUIPlugin.DynamicEitr.Value ||
                __instance == null ||
                __instance.m_eitrAnimator == null)
                return;

            __instance.m_eitrAnimator.SetBool(
                "Visible",
                true);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateAdrenaline")]
    internal static class SmoothUIAdrenalineFadePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (!WiresSmoothUIPlugin.HudEnabled.Value ||
                !WiresSmoothUIPlugin.DynamicAdrenaline.Value ||
                __instance == null ||
                __instance.m_adrenalineAnimator == null)
                return;

            __instance.m_adrenalineAnimator.SetBool(
                "Visible",
                true);
        }
    }
}
