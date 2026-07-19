using UnityEngine;

namespace ClutchFPS.Player
{
    /// The local player's chosen display name, persisted between sessions.
    /// Set from the connect menu before hosting/joining.
    public static class PlayerIdentity
    {
        public static string LocalName
        {
            get => PlayerPrefs.GetString("player_name", "Player");
            set => PlayerPrefs.SetString("player_name",
                string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim());
        }
    }
}
