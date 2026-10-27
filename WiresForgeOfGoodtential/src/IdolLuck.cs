using UnityEngine;

namespace WiresForgeOfGoodtential
{
    // A gentle pity streak: every idol lost in a row makes the next one a
    // little more likely to be kept (+4 points, up to +12). Keeping an idol
    // resets it. Like vanilla's own luck streaks it only takes the edge off
    // a bad run; it lasts for the play session, per character.
    internal static class IdolLuck
    {
        private const float PerLoss = 0.04f;
        private const int MaxLosses = 3;

        private static long _player;
        private static int _losses;

        internal static int Losses
        {
            get
            {
                long id = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0;
                if (id != _player)
                {
                    _player = id;
                    _losses = 0;
                }
                return _losses;
            }
        }

        internal static float Bonus => PerLoss * Losses;

        internal static void Record(bool kept)
        {
            int losses = Losses;
            _losses = kept ? 0 : Mathf.Min(MaxLosses, losses + 1);
        }
    }
}
