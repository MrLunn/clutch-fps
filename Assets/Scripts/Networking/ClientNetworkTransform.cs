using Unity.Netcode.Components;

namespace ClutchFPS.Networking
{
    /// Owner-authoritative transform sync. The stock NetworkTransform is
    /// server-authoritative, which silently breaks client-driven CharacterController
    /// movement (the server never sees it). Fine for a foundation; a competitive
    /// build would move to server-side movement with client prediction instead.
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
