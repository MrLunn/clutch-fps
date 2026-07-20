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
        // Statics survive between sessions, so they must be cleared when a new
        // raid starts or the old progress bar reappears on spawn.
        public static float LocalProgress;
        public static bool LocalInZone;

        public static void ResetLocalState()
        {
            LocalProgress = 0f;
            LocalInZone = false;
        }

        public override void OnNetworkSpawn()
        {
            ResetLocalState();
        }

        private RaidController _occupant;
        private float _timer;
        private Renderer _padRenderer;
        private Light _padLight;
        private MaterialPropertyBlock _block;

        private void Start()
        {
            _padRenderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            // Nearest point light doubles as the pad's glow.
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.name == "ExtractGlow") { _padLight = light; break; }
            }
        }

        private void LateUpdate()
        {
            // Slow breathing pulse marks the pad as an objective; it quickens
            // while someone is extracting.
            float rate = LocalInZone ? 6f : 1.6f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * rate);

            if (_padLight != null)
            {
                _padLight.intensity = Mathf.Lerp(1.4f, 3.2f, pulse);
            }
            if (_padRenderer != null)
            {
                _padRenderer.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", Color.Lerp(
                    new Color(0.12f, 0.45f, 0.20f), new Color(0.35f, 1f, 0.5f), pulse));
                _padRenderer.SetPropertyBlock(_block);
            }
        }

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
