using ClutchFPS.Core;
using ClutchFPS.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ClutchFPS.Weapons
{
    /// A single equippable weapon instance. Firing is server-authoritative: the owning
    /// client sends the shot's origin/direction, the server performs the actual raycast
    /// and applies damage, then broadcasts the hit result for effects.
    public class Weapon : NetworkBehaviour
    {
        [SerializeField] private WeaponData data;
        [SerializeField] private WeaponDatabase database;
        [SerializeField] private LayerMask hittableMask = ~0;

        /// Loot can swap this slot's weapon variant: the synced index selects
        /// from the database; -1 means the serialized default.
        private readonly NetworkVariable<short> _dataIndex = new(-1,
            writePerm: NetworkVariableWritePermission.Server);

        public WeaponData Data
        {
            get
            {
                if (_dataIndex.Value >= 0 && database != null)
                {
                    var variant = database.Get(_dataIndex.Value);
                    if (variant != null) return variant;
                }
                return data;
            }
        }

        public int VariantIndex => _dataIndex.Value;

        /// Server-side: swap this slot to a database variant, fresh mag.
        public void ServerSetWeaponData(int index)
        {
            if (!IsServer) return;
            CancelInvoke(nameof(FinishReloadServer));
            _isReloading.Value = false;
            _dataIndex.Value = (short)index;
            _currentAmmo.Value = Data.magazineSize;
        }

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

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private float _kick;
        private MouseLook _mouseLook;

        // Bloom (0..1): grows per shot, decays while not firing. The server copy
        // drives the authoritative spread; the local copy scales recoil feel.
        private float _serverBloom;
        private float _localBloom;

        // ADS: set by the controller each frame; blend eases the view model
        // between hip rest position and the centered aim position.
        private bool _aiming;
        private float _aimBlend;

        public bool IsAiming => _aiming;
        public void SetAiming(bool aiming) => _aiming = aiming;

        private FirstPersonMovement _movement;
        private Player.PlayerInventory _inventory;
        private Animator _modelAnimator;
        private PlayableGraph _animationGraph;

        private void Awake()
        {
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
            _mouseLook = GetComponentInParent<MouseLook>();
            _movement = GetComponentInParent<FirstPersonMovement>();
            _inventory = GetComponentInParent<Player.PlayerInventory>();
            _modelAnimator = GetComponentInChildren<Animator>(true);
        }

        public override void OnDestroy()
        {
            if (_animationGraph.IsValid()) _animationGraph.Destroy();
            // Netcode does its own teardown here — must not be skipped.
            base.OnDestroy();
        }

        /// Plays a clip from the weapon model's FBX (bolt/slide/mag motion)
        /// directly via Playables — no animator controller asset needed.
        private void PlayModelAnimation(AnimationClip clip)
        {
            if (clip == null || _modelAnimator == null) return;
            if (_animationGraph.IsValid()) _animationGraph.Destroy();
            _animationGraph = PlayableGraph.Create("WeaponAnim");
            var output = AnimationPlayableOutput.Create(_animationGraph, "WeaponAnim", _modelAnimator);
            var playable = AnimationClipPlayable.Create(_animationGraph, clip);
            output.SetSourcePlayable(playable);
            _animationGraph.Play();
        }

        private bool OwnerIsCrouching => _movement != null && _movement.IsCrouching;

        /// Owner's movement as a fraction of walk speed: ~0 still, ~1 walking,
        /// >1 sprinting. Sent to the server so spread reflects how you moved.
        private float OwnerMoveFactor =>
            _movement != null ? _movement.PlanarSpeed / Mathf.Max(0.1f, _movement.walkSpeed) : 0f;

        /// Holstering hides the view model rather than deactivating the
        /// GameObject: Netcode never initializes NetworkBehaviours that spawn
        /// on inactive objects, which silently bricked the pistol.
        public void SetHolstered(bool holstered)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = !holstered;
            }
        }

        private void Update()
        {
            // ADS blend eases between hip and aim positions.
            _aimBlend = Mathf.MoveTowards(_aimBlend, _aiming ? 1f : 0f, 8f * Time.deltaTime);
            Vector3 basePosition = Vector3.Lerp(_restPosition, Data.adsPosition, _aimBlend);

            // Kickback + vibration: snap back on fire with a randomized shake
            // that damps out as the weapon eases forward to rest.
            _kick = Mathf.MoveTowards(_kick, 0f, Data.kickbackRecoverSpeed * Time.deltaTime);
            float jitter = _kick * 0.006f * (1f - _aimBlend * 0.5f);
            transform.localPosition = basePosition
                - Vector3.forward * (_kick * Data.kickbackDistance)
                + new Vector3(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter), 0f);
            float muzzleRise = 30f * Data.kickbackDistance;
            transform.localRotation = _restRotation * Quaternion.Euler(
                -muzzleRise * _kick + Random.Range(-0.8f, 0.8f) * _kick,
                Random.Range(-0.8f, 0.8f) * _kick,
                Random.Range(-1.5f, 1.5f) * _kick);

            float recover = Data.bloomRecoverPerSecond * Time.deltaTime;
            _serverBloom = Mathf.MoveTowards(_serverBloom, 0f, recover);
            _localBloom = Mathf.MoveTowards(_localBloom, 0f, recover);
        }

        /// Owner-side bloom (0..1), read by the HUD to expand the crosshair.
        public float CurrentBloom => _localBloom;

        public FireMode CurrentFireMode =>
            Data.availableFireModes != null && Data.availableFireModes.Length > 0
                ? Data.availableFireModes[Mathf.Clamp(_fireModeIndex, 0, Data.availableFireModes.Length - 1)]
                : FireMode.Single;

        public void CycleFireMode()
        {
            if (Data.availableFireModes == null || Data.availableFireModes.Length < 2) return;
            _fireModeIndex = (_fireModeIndex + 1) % Data.availableFireModes.Length;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _currentAmmo.Value = Data.magazineSize;
            }
            _currentAmmo.OnValueChanged += (_, _) => AmmoChanged?.Invoke();
        }

        /// Call from the local owning client's input handling.
        public void TryFire(Vector3 origin, Vector3 direction)
        {
            if (!IsOwner) return;
            if (_isReloading.Value || _currentAmmo.Value <= 0) return;
            if (Time.time < _nextFireTime) return;

            _nextFireTime = Time.time + 1f / Mathf.Max(Data.fireRate, 0.01f);
            FireServerRpc(origin, direction.normalized, OwnerIsCrouching, _aiming, OwnerMoveFactor);
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
            for (int i = 0; i < Data.burstCount; i++)
            {
                if (_isReloading.Value || _currentAmmo.Value <= 0) break;
                FireServerRpc(aim.position, aim.forward, OwnerIsCrouching, _aiming, OwnerMoveFactor);
                yield return new WaitForSeconds(Data.burstShotInterval);
            }
            _bursting = false;
            _nextFireTime = Time.time + 1f / Mathf.Max(Data.fireRate, 0.01f);
        }

        /// Reserve rounds available for this weapon in the carrier's inventory.
        public int ReserveAmmo => _inventory != null ? _inventory.CountOf(Data.ammoItemId) : 0;

        public void TryReload()
        {
            if (!IsOwner || _isReloading.Value) return;
            if (_currentAmmo.Value >= Data.magazineSize)
            {
                HitFeedback.RegisterMagFull();
                return;
            }
            if (ReserveAmmo <= 0)
            {
                HitFeedback.RegisterNoAmmo();
                return;
            }
            ReloadServerRpc();
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 origin, Vector3 direction, bool crouched, bool aimed, float moveFactor, ServerRpcParams rpcParams = default)
        {
            if (_isReloading.Value || _currentAmmo.Value <= 0) return;

            _currentAmmo.Value--;

            // Base stance spread — applies to every shot, including the first,
            // so accuracy depends on how you're standing/moving:
            //  - moving (walk/run): a real penalty, scaled by speed
            //  - standing still: a small base spread
            //  - crouched & still: tighter than standing (the crouch bonus),
            //    but crouch-walking still takes the movement penalty.
            bool moving = moveFactor > 0.2f;
            float baseSpread = moving
                ? Data.stillSpread + Data.movingSpread * Mathf.Clamp(moveFactor, 0.4f, 1.8f)
                : Data.stillSpread * (crouched ? Data.crouchSpreadMultiplier : 1f);

            // Bloom spread from sustained fire; crouch tightens it only when still.
            float bloomSpread = Data.spreadDegrees * _serverBloom;
            if (crouched && !moving) bloomSpread *= Data.crouchSpreadMultiplier;

            float totalSpread = baseSpread + bloomSpread;
            if (aimed) totalSpread *= Data.adsSpreadMultiplier;

            Vector3 spreadDirection = ApplySpread(direction, totalSpread);
            _serverBloom = Mathf.Min(1f, _serverBloom + Data.bloomPerShot);
            Vector3 hitPoint = origin + spreadDirection * Data.range;

            byte hitType = 0; // 0 = air, 1 = world, 2 = flesh
            bool headshot = false;
            bool killed = false;
            Vector3 hitNormal = -spreadDirection;
            if (Physics.Raycast(origin, spreadDirection, out RaycastHit hit, Data.range, hittableMask))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    hitType = 2;
                    float damage = Data.damage;
                    if (hit.collider.TryGetComponent<HitZone>(out var zone))
                    {
                        headshot = true;
                        damage = zone.instantKill ? 99999f : damage * zone.damageMultiplier;
                    }
                    ulong attackerId = rpcParams.Receive.SenderClientId;
                    damageable.TakeDamage(damage, attackerId);
                    killed = damageable is Health targetHealth && targetHealth.CurrentHealth <= 0f;
                    // Tell the victim which way the shot came from.
                    hit.collider.GetComponentInParent<Player.PlayerRespawn>()?.ServerReportDamage(origin);
                }
                else
                {
                    hitType = 1;
                }
            }

            HitEffectClientRpc(origin, hitPoint, hitNormal, hitType, headshot, killed);

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
            if (_currentAmmo.Value >= Data.magazineSize && !_isReloading.Value) return false;
            CancelInvoke(nameof(FinishReloadServer));
            _isReloading.Value = false;
            _currentAmmo.Value = Data.magazineSize;
            return true;
        }

        private void StartReloadServer()
        {
            if (_isReloading.Value) return;
            // No reserve, no reload — ammo is a real resource now.
            if (_inventory != null && _inventory.CountOf(Data.ammoItemId) <= 0) return;
            _isReloading.Value = true;
            ReloadSoundClientRpc();
            Invoke(nameof(FinishReloadServer), Data.reloadTime);
        }

        [ClientRpc]
        private void ReloadSoundClientRpc()
        {
            if (Data.reloadSound != null)
            {
                AudioSource.PlayClipAtPoint(Data.reloadSound, transform.position, 0.7f);
            }
            PlayModelAnimation(Data.reloadAnimation);
        }

        private void FinishReloadServer()
        {
            int needed = Data.magazineSize - _currentAmmo.Value;
            int loaded = _inventory != null
                ? _inventory.ServerTakeItem(Data.ammoItemId, needed)
                : needed;
            _currentAmmo.Value += loaded;
            _isReloading.Value = false;
        }

        [ClientRpc]
        private void HitEffectClientRpc(Vector3 origin, Vector3 hitPoint, Vector3 hitNormal,
            byte hitType, bool headshot, bool killed)
        {
            // Tracer starts at this weapon's muzzle so it reads correctly from
            // every perspective, even though the actual ray came from the camera.
            Vector3 muzzle = transform.position + transform.forward * 0.5f;
            SpawnTracer(muzzle, hitPoint);
            PlayModelAnimation(Data.fireAnimation);

            // VFX must never kill the RPC (it carries hitmarkers and sounds):
            // any failure falls back to the built-in effect.
            try
            {
                if (Data.muzzleFlashPrefab != null)
                {
                    // Parented so it rides the weapon; destroyed fast because
                    // WarFX muzzle flashes loop while alive.
                    var mf = Instantiate(Data.muzzleFlashPrefab, muzzle, transform.rotation, transform);
                    Destroy(mf, 0.06f);
                }
                else
                {
                    SpawnMuzzleFlash(muzzle);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Muzzle flash VFX failed: {e.Message}");
                SpawnMuzzleFlash(muzzle);
            }

            if (Data.fireSound != null)
            {
                AudioSource.PlayClipAtPoint(Data.fireSound, muzzle, Data.fireVolume);
            }
            else
            {
                PlayShotSound(muzzle);
            }

            if (hitType > 0)
            {
                GameObject impactPrefab = hitType == 2 ? Data.fleshImpactPrefab : Data.worldImpactPrefab;
                try
                {
                    if (impactPrefab != null)
                    {
                        var impact = Instantiate(impactPrefab, hitPoint, Quaternion.LookRotation(hitNormal));
                        // World hits keep their bullet-hole mark around; flesh bursts are brief.
                        Destroy(impact, hitType == 2 ? Data.vfxLifetime : Data.impactLifetime);
                    }
                    else
                    {
                        SpawnImpact(hitPoint, flesh: hitType == 2);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Impact VFX failed: {e.Message}");
                    SpawnImpact(hitPoint, flesh: hitType == 2);
                }
                if (Data.impactSound != null)
                {
                    AudioSource.PlayClipAtPoint(Data.impactSound, hitPoint, 0.6f);
                }
            }
            if (IsOwner && hitType == 2)
            {
                HitFeedback.RegisterHit(headshot, killed);
                if (killed && headshot)
                {
                    // Skip the plain hit tick; the headshot-kill sound is the
                    // whole reward and shouldn't fight another cue over it.
                    FeedbackAudio.PlayHeadshotKill(muzzle);
                }
                else
                {
                    FeedbackAudio.PlayHit(muzzle, headshot);
                    if (killed) FeedbackAudio.PlayKill(muzzle);
                }
            }

            _kick = 1f;
            if (IsOwner && _mouseLook != null)
            {
                // First shots barely kick; sustained fire climbs harder. Crouching steadies.
                float recoilScale = Mathf.Lerp(Data.recoilMinScale, Data.recoilMaxScale, _localBloom);
                if (OwnerIsCrouching) recoilScale *= Data.crouchRecoilMultiplier;
                if (_aiming) recoilScale *= Data.adsRecoilMultiplier;
                _mouseLook.AddRecoil(Data.recoilPitchKick * recoilScale, Data.recoilYawKick * recoilScale);
                _localBloom = Mathf.Min(1f, _localBloom + Data.bloomPerShot);
                Player.CameraShake.Add(0.06f + 0.04f * recoilScale);
            }
        }

        private static Material _tracerMaterial;

        private void SpawnTracer(Vector3 from, Vector3 to)
        {
            if (_tracerMaterial == null && Data.tracerMaterial != null)
            {
                _tracerMaterial = Data.tracerMaterial;
            }
            if (_tracerMaterial == null)
            {
                // Editor-only fallback; these shaders are stripped from builds.
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) return;
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
                // Editor-only fallback; builds use the WarFX prefabs instead.
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) return;
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
