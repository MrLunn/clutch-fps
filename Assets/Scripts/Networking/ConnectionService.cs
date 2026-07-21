using Unity.Services.Multiplayer;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Wraps the Unity Multiplayer Sessions API (Relay-backed) so hosting and
    /// joining work over the internet with a short join code and no port
    /// forwarding. Creating/joining a session drives Netcode for GameObjects
    /// automatically. Async work updates synchronous flags for the IMGUI menu.
    public static class ConnectionService
    {
        public enum State { Idle, Connecting, Connected, Error }

        public const int MaxPlayers = 4;

        public static State Status { get; private set; } = State.Idle;
        public static string JoinCode { get; private set; } = "";
        public static string LastError { get; private set; } = "";

        public static bool Busy => Status == State.Connecting;

        private static ISession _session;

        public static async void Host()
        {
            if (Busy) return;
            Status = State.Connecting;
            LastError = "";
            try
            {
                var options = new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                JoinCode = _session.Code;
                Status = State.Connected;
            }
            catch (System.Exception e) { Fail(e); }
        }

        public static async void Join(string code)
        {
            if (Busy) return;
            Status = State.Connecting;
            LastError = "";
            try
            {
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim());
                JoinCode = _session.Code;
                Status = State.Connected;
            }
            catch (System.Exception e) { Fail(e); }
        }

        public static async void Leave()
        {
            try { if (_session != null) await _session.LeaveAsync(); }
            catch (System.Exception) { }
            _session = null;
            JoinCode = "";
            Status = State.Idle;
        }

        private static void Fail(System.Exception e)
        {
            LastError = e.Message;
            Status = State.Error;
            _session = null;
            Debug.LogWarning($"ConnectionService: {e}");
        }
    }
}
