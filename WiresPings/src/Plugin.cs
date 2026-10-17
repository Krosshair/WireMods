using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WiresPings
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.pings";
        public const string Name = "Wire's Pings";
        public const string Version = "0.1.2";

        private static readonly RaycastHit[] Hits =
            new RaycastHit[64];

        private static readonly RaycastHitComparer HitComparer =
            new RaycastHitComparer();

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _pingKey;
        private ConfigEntry<float> _maxDistance;

        private int _pingMask;

        private void Awake()
        {
            _enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable aiming pings.");

            _pingKey = Config.Bind(
                "General",
                "Ping Hotkey",
                new KeyboardShortcut(KeyCode.B),
                "Ping the exact world position under the centre crosshair.");

            _maxDistance = Config.Bind(
                "General",
                "Maximum Ping Distance",
                1000f,
                new ConfigDescription(
                    "Maximum distance in metres for aimed pings.",
                    new AcceptableValueRange<float>(10f, 1000f)));

            _pingMask =
                LayerMask.GetMask(
                    "Default",
                    "static_solid",
                    "Default_small",
                    "piece",
                    "piece_nonsolid",
                    "terrain",
                    "vehicle",
                    "character",
                    "character_net",
                    "character_ghost",
                    "hitbox");

            Logger.LogInfo(
                Name + " " + Version + " loaded.");
        }

        private void Update()
        {
            if (!_enabled.Value ||
                !_pingKey.Value.IsDown())
                return;

            Player player =
                Player.m_localPlayer;

            if (player == null ||
                GameCamera.instance == null ||
                Chat.instance == null)
                return;

            if (Console.IsVisible() ||
                TextInput.IsVisible() ||
                Chat.instance.HasFocus() ||
                InventoryGui.IsVisible() ||
                (Minimap.instance != null &&
                 Minimap.instance.m_mode == Minimap.MapMode.Large))
            {
                return;
            }

            Camera camera =
                Utils.GetMainCamera();

            if (camera == null)
                return;

            int hitCount =
                Physics.RaycastNonAlloc(
                    camera.transform.position,
                    camera.transform.forward,
                    Hits,
                    _maxDistance.Value,
                    _pingMask,
                    QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
                return;

            Array.Sort(
                Hits,
                0,
                hitCount,
                HitComparer);

            for (int i = 0;
                 i < hitCount;
                 ++i)
            {
                Collider collider =
                    Hits[i].collider;

                if (collider == null)
                    continue;

                Player hitPlayer =
                    collider.GetComponentInParent<Player>();

                if (hitPlayer == player)
                    continue;

                Vector3 pingPosition =
                    Hits[i].point;

                pingPosition.y =
                    player.transform.position.y;

                MoveExistingLocalPing(
                    pingPosition);

                Chat.instance.SendPing(
                    Hits[i].point);

                return;
            }
        }

        private static void MoveExistingLocalPing(
            Vector3 position)
        {
            if (ZNet.instance == null ||
                Chat.instance == null)
            {
                return;
            }

            long senderId =
                ZNet.instance.LocalPlayerCharacterID.UserID;

            List<Chat.WorldTextInstance> worldTexts =
                Chat.instance.WorldTexts;

            for (int i = 0;
                 i < worldTexts.Count;
                 ++i)
            {
                Chat.WorldTextInstance worldText =
                    worldTexts[i];

                if (worldText == null ||
                    worldText.m_talkerID != senderId)
                {
                    continue;
                }

                worldText.m_go = null;
                worldText.m_position = position;
                worldText.m_timer = 0f;
                return;
            }
        }

        private sealed class RaycastHitComparer :
            IComparer<RaycastHit>
        {
            public int Compare(
                RaycastHit a,
                RaycastHit b)
            {
                return
                    a.distance.CompareTo(
                        b.distance);
            }
        }
    }
}
