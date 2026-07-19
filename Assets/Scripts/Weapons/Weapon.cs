using ClutchFPS.Core;
using ClutchFPS.Player;
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

        [Header("Feel")]
        [SerializeField] private float recoilPitchKick = 0.6f;
        [SerializeField] private float recoilYawKick = 0.2f;
        [SerializeField] private float kickbackDistance = 0.07f;
        [SerializeField] private float kickbackRecoverSpeed = 8f;

        private Vector3 _restPosition;
        private float _kick;
        private MouseLook _mouseLook;

        // Bloom (0..1): grows per shot, decays while not firing. The server copy
        // drives the authoritative spread; the local copy scales recoil feel.
        private float _serverBloom;
        private float _localBloom;

        private FirstPersonMovement _movement;

        private void Awake()
        {
            _restPosition = transform.localPosition;
            _mouseLook = GetComponentInParent<MouseLook>();
            _movement = GetComponentInParent<FirstPersonMovement>();
        }

        private bool OwnerIsCrouching => _movement != null && _movement.IsCrouching;

        private void Update()
        {
            // Kickback animation: snap back on fire, ease forward to rest.
            _kick = Mathf.MoveTowards(_kick, 0f, kickbackRecoverSpeed * Time.deltaTime);
            transform.localPosition = _restPosition - Vector3.forward * (_kick * kickbackDistance);

            float recover = data.bloomRecoverPerSecond * Time.deltaTime;
            _serverBloom = Mathf.MoveTowards(_serverBloom, 0f, recover);
            _localBloom = Mathf.MoveTowards(_localBloom, 0f, recover);
        }

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
            FireServerRpc(origin, direction.normalized, OwnerIsCrouching);
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
                FireServerRpc(aim.position, aim.forward, OwnerIsCrouching);
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
        private void FireServerRpc(Vector3 origin, Vector3 direction, bool crouched, ServerRpcParams rpcParams = default)
        {
            if (_isReloading.Value || _currentAmmo.Value <= 0) return;

            _currentAmmo.Value--;

            float maxSpread = data.spreadDegrees * (crouched ? 0.45f : 1f);
            Vector3 spreadDirection = ApplySpread(direction, maxSpread * _serverBloom);
            _serverBloom = Mathf.Min(1f, _serverBloom + data.bloomPerShot);
            Vector3 hitPoint = origin + spreadDirection * data.range;

            byte hitType = 0; // 0 = air, 1 = world, 2 = flesh
            if (Physics.Raycast(origin, spreadDirection, out RaycastHit hit, data.range, hittableMask))
            {
                hitPoint = hit.point;
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    hitType = 2;
                    float damage = data.damage;
                    if (hit.collider.TryGetComponent<HitZone>(out var zone))
                    {
                        damage = zone.instantKill ? 99999f : damage * zone.damageMultiplier;
                    }
                    ulong attackerId = rpcParams.Receive.SenderClientId;
                    damageable.TakeDamage(damage, attackerId);
                }
                else
                {
                    hitType = 1;
                }
            }

            HitEffectClientRpc(origin, hitPoint, hitType);

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
        private void HitEffectClientRpc(Vector3 origin, Vector3 hitPoint, byte hitType)
        {
            // Tracer starts at this weapon's muzzle so it reads correctly from
            // every perspective, even though the actual ray came from the camera.
            Vector3 muzzle = transform.position + transform.forward * 0.5f;
            SpawnTracer(muzzle, hitPoint);
            SpawnMuzzleFlash(muzzle);
            PlayShotSound(muzzle);

            if (hitType > 0)
            {
                SpawnImpact(hitPoint, flesh: hitType == 2);
            }
            if (IsOwner && hitType == 2)
            {
                HitFeedback.RegisterHit();
            }

            _kick = 1f;
            if (IsOwner && _mouseLook != null)
            {
                // First shots barely kick; sustained fire climbs harder. Crouching steadies.
                float recoilScale = Mathf.Lerp(0.35f, 1.25f, _localBloom);
                if (OwnerIsCrouching) recoilScale *= 0.7f;
                _mouseLook.AddRecoil(recoilPitchKick * recoilScale, recoilYawKick * recoilScale);
                _localBloom = Mathf.Min(1f, _localBloom + data.bloomPerShot);
            }
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

        private static Material _fleshImpactMaterial;
        private static Material _worldImpactMaterial;

        private static void SpawnImpact(Vector3 position, bool flesh)
        {
            if (_fleshImpactMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _fleshImpactMaterial = new Material(shader) { color = new Color(0.65f, 0.05f, 0.05f) };
                _worldImpactMaterial = new Material(shader) { color = new Color(0.7f, 0.68f, 0.6f) };
            }

            var go = new GameObject("Impact");
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = 0.3f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.gravityModifier = 1.5f;
            main.maxParticles = 24;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            go.GetComponent<ParticleSystemRenderer>().material =
                flesh ? _fleshImpactMaterial : _worldImpactMaterial;

            ps.Play();
            Destroy(go, 1f);
        }

        private static void SpawnMuzzleFlash(Vector3 position)
        {
            var flash = new GameObject("MuzzleFlash");
            flash.transform.position = position;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.45f);
            light.intensity = 1.3f;
            light.range = 2.5f;
            Destroy(flash, 0.03f);
        }

        private static AudioClip _shotClip;

        private static void PlayShotSound(Vector3 position)
        {
            if (_shotClip == null)
            {
                // Procedural gunshot: sharp noise crack with an exponential decay
                // plus a low-frequency thump. Placeholder until real SFX exist.
                const int sampleRate = 44100;
                const float duration = 0.18f;
                int sampleCount = (int)(sampleRate * duration);
                var samples = new float[sampleCount];
                var random = new System.Random(12345);
                float previous = 0f;
                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (float)i / sampleRate;
                    float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                    // One-pole lowpass keeps the crack from being pure hiss.
                    previous = Mathf.Lerp(previous, noise, 0.35f);
                    float crack = previous * Mathf.Exp(-t * 45f);
                    float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 25f) * 0.7f;
                    samples[i] = Mathf.Clamp(crack + thump, -1f, 1f) * 0.8f;
                }
                _shotClip = AudioClip.Create("GunshotProcedural", sampleCount, 1, sampleRate, false);
                _shotClip.SetData(samples, 0);
            }

            AudioSource.PlayClipAtPoint(_shotClip, position, 0.6f);
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
