using ClutchFPS.Core;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Weapons
{
    /// A single equippable weapon instance. Firing is server-authoritative: the owning
    /// client sends the shot's origin/direction, the server performs the actual raycast
    /// and applies damage, then broadcasts the hit result for effects.
    public class Weapon : NetworkBehaviour
    {
        [SerializeField] private WeaponData data;
        [SerializeField] private LayerMask hittableMask = ~0;

        public WeaponData Data => data;

        private readonly NetworkVariable<int> _currentAmmo = new(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _isReloading = new(
            writePerm: NetworkVariableWritePermission.Server);

        public int CurrentAmmo => _currentAmmo.Value;
        public bool IsReloading => _isReloading.Value;

        public event System.Action AmmoChanged;

        private float _nextFireTime;
        private int _fireModeIndex;
        private bool _bursting;

        public FireMode CurrentFireMode =>
            data.availableFireModes != null && data.availableFireModes.Length > 0
                ? data.availableFireModes[Mathf.Clamp(_fireModeIndex, 0, data.availableFireModes.Length - 1)]
                : FireMode.Single;

        public void CycleFireMode()
        {
            if (data.availableFireModes == null || data.availableFireModes.Length < 2) return;
            _fireModeIndex = (_fireModeIndex + 1) % data.availableFireModes.Length;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _currentAmmo.Value = data.magazineSize;
            }
            _currentAmmo.OnValueChanged += (_, _) => AmmoChanged?.Invoke();
        }

        /// Call from the local owning client's input handling.
        public void TryFire(Vector3 origin, Vector3 direction)
        {
            if (!IsOwner) return;
            if (_isReloading.Value || _currentAmmo.Value <= 0) return;
            if (Time.time < _nextFireTime) return;

            _nextFireTime = Time.time + 1f / Mathf.Max(data.fireRate, 0.01f);
            FireServerRpc(origin, direction.normalized);
        }

        /// Fires a full burst; aim is re-read per shot so the burst tracks the camera.
        public void TryFireBurst(Transform aim)
        {
            if (!IsOwner || _bursting) return;
            if (_isReloading.Value || _currentAmmo.Value <= 0) return;
            if (Time.time < _nextFireTime) return;
            StartCoroutine(BurstRoutine(aim));
        }

        private System.Collections.IEnumerator BurstRoutine(Transform aim)
        {
            _bursting = true;
            for (int i = 0; i < data.burstCount; i++)
            {
                if (_isReloading.Value || _currentAmmo.Value <= 0) break;
                FireServerRpc(aim.position, aim.forward);
                yield return new WaitForSeconds(data.burstShotInterval);
            }
            _bursting = false;
            _nextFireTime = Time.time + 1f / Mathf.Max(data.fireRate, 0.01f);
        }

        public void TryReload()
        {
            if (!IsOwner) return;
            if (_isReloading.Value || _currentAmmo.Value >= data.magazineSize) return;
            ReloadServerRpc();
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            if (_isReloading.Value || _currentAmmo.Value <= 0) return;

            _currentAmmo.Value--;

            Vector3 spreadDirection = ApplySpread(direction, data.spreadDegrees);
            Vector3 hitPoint = origin + spreadDirection * data.range;

            if (Physics.Raycast(origin, spreadDirection, out RaycastHit hit, data.range, hittableMask))
            {
                hitPoint = hit.point;
                if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
                {
                    ulong attackerId = rpcParams.Receive.SenderClientId;
                    damageable.TakeDamage(data.damage, attackerId);
                }
            }

            HitEffectClientRpc(origin, hitPoint);

            if (_currentAmmo.Value <= 0)
            {
                StartReloadServer();
            }
        }

        [ServerRpc]
        private void ReloadServerRpc()
        {
            StartReloadServer();
        }

        /// Server-side instant refill (used by ammo pickups). Returns false if already full.
        public bool ServerRefillAmmo()
        {
            if (!IsServer) return false;
            if (_currentAmmo.Value >= data.magazineSize && !_isReloading.Value) return false;
            CancelInvoke(nameof(FinishReloadServer));
            _isReloading.Value = false;
            _currentAmmo.Value = data.magazineSize;
            return true;
        }

        private void StartReloadServer()
        {
            if (_isReloading.Value) return;
            _isReloading.Value = true;
            Invoke(nameof(FinishReloadServer), data.reloadTime);
        }

        private void FinishReloadServer()
        {
            _currentAmmo.Value = data.magazineSize;
            _isReloading.Value = false;
        }

        [ClientRpc]
        private void HitEffectClientRpc(Vector3 origin, Vector3 hitPoint)
        {
            // Tracer starts at this weapon's muzzle so it reads correctly from
            // every perspective, even though the actual ray came from the camera.
            Vector3 muzzle = transform.position + transform.forward * 0.5f;
            SpawnTracer(muzzle, hitPoint);
        }

        private static Material _tracerMaterial;

        private static void SpawnTracer(Vector3 from, Vector3 to)
        {
            if (_tracerMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _tracerMaterial = new Material(shader) { color = new Color(1f, 0.85f, 0.3f) };
            }

            var tracer = new GameObject("Tracer");
            var line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.02f;
            line.endWidth = 0.005f;
            line.material = _tracerMaterial;
            Destroy(tracer, 0.07f);
        }

        private static Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
        {
            if (spreadDegrees <= 0f) return direction;
            float yaw = Random.Range(-spreadDegrees, spreadDegrees);
            float pitch = Random.Range(-spreadDegrees, spreadDegrees);
            Quaternion spreadRotation = Quaternion.Euler(pitch, yaw, 0f);
            return spreadRotation * direction;
        }
    }
}
