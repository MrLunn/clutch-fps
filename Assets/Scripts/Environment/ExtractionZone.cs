using ClutchFPS.Player;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// A pad the player must stand on for holdTime to extract (bank their
    /// gear to the stash and leave). Server-authoritative; the owning client
    /// reads progress off its own RaidController-adjacent state via trigger.
    [RequireComponent(typeof(Collider))]
    public class ExtractionZone : NetworkBehaviour
    {
        [SerializeField] private float holdTime = 5f;

        // Progress for the local player, 0..1, mirrored so the HUD can draw it.
        public static float LocalProgress;
        public static bool LocalInZone;

        private RaidController _occupant;
        private float _timer;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            if (other.TryGetComponent<RaidController>(out var raid)) { _occupant = raid; _timer = 0f; }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            if (other.TryGetComponent<RaidController>(out var raid) && raid == _occupant)
            {
                _occupant = null;
                _timer = 0f;
                ProgressClientRpc(0f, raid.OwnerClientId);
            }
        }

        private void Update()
        {
            if (!IsServer || _occupant == null) return;
            if (_occupant.HasExtracted) { _occupant = null; return; }

            _timer += Time.deltaTime;
            ProgressClientRpc(Mathf.Clamp01(_timer / holdTime), _occupant.OwnerClientId);
            if (_timer >= holdTime)
            {
                _occupant.ServerExtract();
                _occupant = null;
            }
        }

        [ClientRpc]
        private void ProgressClientRpc(float progress, ulong targetClient)
        {
            if (NetworkManager.Singleton.LocalClientId != targetClient) return;
            LocalProgress = progress;
            LocalInZone = progress > 0f;
        }
    }
}
