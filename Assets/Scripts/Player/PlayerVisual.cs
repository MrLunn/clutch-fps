using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Player
{
    /// Hides the body mesh for the owning player so it doesn't block the
    /// first-person camera; everyone else sees the capsule.
    public class PlayerVisual : NetworkBehaviour
    {
        [SerializeField] private MeshRenderer bodyRenderer;

        public override void OnNetworkSpawn()
        {
            if (bodyRenderer != null)
            {
                bodyRenderer.enabled = !IsOwner;
            }
        }
    }
}
