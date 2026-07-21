using ClutchFPS.Core;
using ClutchFPS.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace ClutchFPS.Environment
{
    /// Server-driven PvE enemy: patrols near its home point, chases the
    /// nearest visible player, and fires hitscan shots with spread. Position
    /// syncs via the stock (server-authoritative) NetworkTransform. Dies like
    /// a target (hide + colliders off) and respawns at home.
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public class EnemyAI : NetworkBehaviour
    {
        public const ulong AiClientId = ulong.MaxValue;

        [Header("Senses")]
        [SerializeField] private float sightRange = 16f;
        [SerializeField] private float attackRange = 13f;
        [SerializeField] private float eyeHeight = 1.5f;
        [Tooltip("Total vision cone. Outside it the enemy is blind, so flanking " +
                 "and breaking contact actually work.")]
        [SerializeField] private float fieldOfViewDegrees = 110f;
        [Tooltip("Within this range the enemy notices you even outside its cone " +
                 "(footsteps/presence). Line of sight is still required.")]
        [SerializeField] private float proximityAwareness = 4.5f;
        [Tooltip("How long an enemy keeps hunting after losing sight, before " +
                 "giving up and returning to patrol.")]
        [SerializeField] private float memoryDuration = 4f;

        [Header("Combat")]
        [SerializeField] private float damage = 12f;
        [SerializeField] private float fireCooldown = 1.2f;
        [SerializeField] private float aimSpreadDegrees = 4f;

        [Tooltip("Seconds between spotting a target and the first shot.")]
        [SerializeField] private float reactionTime = 0.95f;
        [Tooltip("Random aim error in metres at the target, independent of range — " +
                 "angular spread alone makes close-range shots unmissable.")]
        [SerializeField] private float aimErrorMetres = 0.8f;
        [Tooltip("Fraction of shots deliberately thrown wide.")]
        [Range(0f, 1f)][SerializeField] private float missChance = 0.35f;
        [Tooltip("Shots per burst before a longer pause.")]
        [SerializeField] private int burstSize = 3;
        [SerializeField] private float burstPause = 1.8f;
        [SerializeField] private AudioClip fireSound;
        [SerializeField] private Material tracerMaterial;

        [Header("Life")]
        [SerializeField] private float patrolRadius = 6f;
        [SerializeField] private float respawnDelay = 15f;
        [SerializeField] private Transform visual;

        private NavMeshAgent _agent;
        private Health _health;
        private Collider[] _colliders;
        private Vector3 _home;
        private float _nextFireTime;
        private float _nextTargetScan;
        private PlayerRespawn _target;
        private PlayerRespawn _previousTarget;
        private float _targetAcquiredTime;
        private float _lastSeenTime;
        private Vector3 _lastKnownPosition;
        private float _stuckTimer;
        private int _shotsInBurst;
        private bool _dead;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                // Clients just render what the server syncs.
                _agent.enabled = false;
                enabled = false;
                return;
            }
            _home = transform.position;
            _health.Died += OnDiedServer;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) _health.Died -= OnDiedServer;
        }

        private void Update()
        {
            if (_dead) return;

            // Spawned inside a crate/container? Then we're off the NavMesh and
            // can never move. Snap to the nearest walkable point once the mesh
            // is baked, retrying each frame until it takes.
            if (!_agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out var navHit, 8f, NavMesh.AllAreas))
                {
                    _agent.Warp(navHit.position);
                    _home = navHit.position;
                }
                return;
            }

            RecoverIfStuck();

            if (Time.time >= _nextTargetScan)
            {
                _nextTargetScan = Time.time + 0.5f;
                var spotted = FindVisiblePlayer();
                if (spotted != null)
                {
                    _target = spotted;
                    _lastSeenTime = Time.time;
                    _lastKnownPosition = spotted.transform.position;
                }
                else if (_target != null && Time.time > _lastSeenTime + memoryDuration)
                {
                    // Lost them for good — forget and go back to the patrol.
                    _target = null;
                }

                // Freshly spotted targets get a reaction delay before fire.
                if (_target != null && _target != _previousTarget)
                {
                    _targetAcquiredTime = Time.time;
                    _shotsInBurst = 0;
                }
                _previousTarget = _target;
            }

            if (_target == null)
            {
                Patrol();
                return;
            }

            Vector3 targetChest = _target.transform.position + Vector3.up * 1.2f;
            float distance = Vector3.Distance(transform.position, _target.transform.position);
            bool visible = CanSee(_target);

            if (visible && distance <= attackRange)
            {
                _agent.SetDestination(transform.position);
                FaceTarget(targetChest);
                bool reacted = Time.time >= _targetAcquiredTime + reactionTime;
                if (reacted && Time.time >= _nextFireTime)
                {
                    _shotsInBurst++;
                    bool endOfBurst = _shotsInBurst >= burstSize;
                    _nextFireTime = Time.time + (endOfBurst ? burstPause : fireCooldown);
                    if (endOfBurst) _shotsInBurst = 0;
                    FireAt(targetChest);
                }
            }
            else if (visible)
            {
                _agent.SetDestination(_target.transform.position);
            }
            else
            {
                // Out of sight: search the last place they were actually seen,
                // never their live position — that was the wallhack.
                _agent.SetDestination(_lastKnownPosition);
            }
        }

        /// The nearest player this enemy can genuinely see: in range, inside
        /// the vision cone, and with clear line of sight.
        private PlayerRespawn FindVisiblePlayer()
        {
            PlayerRespawn nearest = null;
            float best = sightRange;
            foreach (var player in FindObjectsByType<PlayerRespawn>(FindObjectsSortMode.None))
            {
                if (player.IsDead) continue;
                // Extracted players have left the raid.
                if (player.TryGetComponent<RaidController>(out var raid) && raid.HasExtracted) continue;
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance >= best) continue;
                if (!CanSee(player)) continue;
                best = distance;
                nearest = player;
            }
            return nearest;
        }

        private bool CanSee(PlayerRespawn player)
        {
            if (player == null || player.IsDead) return false;

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 chest = player.transform.position + Vector3.up * 1.2f;
            Vector3 direction = chest - eye;
            if (direction.magnitude > sightRange) return false;

            // Close-quarters awareness: someone right next to you gets noticed
            // regardless of facing (footsteps, presence). The line-of-sight
            // check below still applies, so they can't sense through a wall.
            bool pointBlank = direction.magnitude <= proximityAwareness;

            // Vision cone, measured flat so looking up or down doesn't matter.
            Vector3 flat = direction;
            flat.y = 0f;
            if (!pointBlank && flat.sqrMagnitude > 0.01f &&
                Vector3.Angle(transform.forward, flat) > fieldOfViewDegrees * 0.5f)
            {
                return false;
            }

            if (!Physics.Raycast(eye, direction.normalized, out RaycastHit hit,
                direction.magnitude + 0.5f, ~(1 << 6))) return false;
            return hit.collider.GetComponentInParent<PlayerRespawn>() == player;
        }

        private void FaceTarget(Vector3 targetPoint)
        {
            Vector3 flat = targetPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(flat), 8f * Time.deltaTime);
        }

        private void FireAt(Vector3 targetPoint)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;

            // Aim error in metres at the target keeps close-range shots
            // missable; angular spread alone shrinks to nothing up close.
            float error = aimErrorMetres;
            if (Random.value < missChance) error *= 3f;
            Vector2 offset = Random.insideUnitCircle * error;
            Vector3 aimPoint = targetPoint
                + Vector3.right * offset.x
                + Vector3.up * offset.y;

            Vector3 direction = (aimPoint - eye).normalized;
            float yaw = Random.Range(-aimSpreadDegrees, aimSpreadDegrees);
            float pitch = Random.Range(-aimSpreadDegrees, aimSpreadDegrees);
            direction = Quaternion.Euler(pitch, yaw, 0f) * direction;

            Vector3 hitPoint = eye + direction * sightRange;
            if (Physics.Raycast(eye, direction, out RaycastHit hit, sightRange, ~(1 << 6)))
            {
                hitPoint = hit.point;
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(damage, AiClientId);
                hit.collider.GetComponentInParent<PlayerRespawn>()?.ServerReportDamage(eye);
            }
            ShotEffectClientRpc(eye, hitPoint);
        }

        [ClientRpc]
        private void ShotEffectClientRpc(Vector3 from, Vector3 to)
        {
            if (fireSound != null)
            {
                AudioSource.PlayClipAtPoint(fireSound, from, 0.5f);
            }
            var tracer = new GameObject("EnemyTracer");
            var line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.02f;
            line.endWidth = 0.005f;
            if (tracerMaterial != null) line.material = tracerMaterial;
            Destroy(tracer, 0.07f);
        }

        /// If the agent wants to move but hasn't for a couple of seconds, it's
        /// wedged on geometry — warp it to a clear spot near home to break out.
        /// Standing still to shoot doesn't count.
        private void RecoverIfStuck()
        {
            bool wantsToMove = _agent.hasPath && !_agent.pathPending
                && _agent.remainingDistance > _agent.stoppingDistance + 0.5f;
            if (wantsToMove && _agent.velocity.sqrMagnitude < 0.02f)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer > 2f)
                {
                    _stuckTimer = 0f;
                    if (NavMesh.SamplePosition(_home, out var navHit, 6f, NavMesh.AllAreas))
                    {
                        _agent.Warp(navHit.position);
                    }
                }
            }
            else
            {
                _stuckTimer = 0f;
            }
        }

        private void Patrol()
        {
            if (_agent.pathPending || _agent.remainingDistance > 0.6f) return;
            Vector2 offset = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = _home + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                _agent.SetDestination(navHit.position);
            }
        }

        private void OnDiedServer(ulong attackerClientId)
        {
            _dead = true;
            _agent.ResetPath();
            DropLoot();
            SetDeadClientRpc(true);
            Invoke(nameof(RespawnServer), respawnDelay);
        }

        /// Corpse loot: always a little ammo, sometimes a medkit, occasionally a
        /// weapon — enough that clearing AI feeds the stash without flooding it.
        private void DropLoot()
        {
            Vector3 basePos = transform.position + Vector3.up * 0.4f;

            // Ammo, biased to the common rifle round.
            bool rifleAmmo = Random.value < 0.65f;
            int ammoId = rifleAmmo ? (int)ItemType.Ammo556 : (int)ItemType.Ammo9mm;
            int ammoAmount = rifleAmmo ? Random.Range(15, 31) : Random.Range(10, 21);
            LootSpawner.SpawnItem(Scatter(basePos), ammoId, ammoAmount);

            if (Random.value < 0.3f)
            {
                LootSpawner.SpawnItem(Scatter(basePos), (int)ItemType.Medkit, 1);
            }
            // Occasional weapon, rarity-weighted so rares/epics feel earned.
            if (Random.value < 0.18f)
            {
                var (slot, variant) = RollWeaponDrop();
                LootSpawner.SpawnWeapon(Scatter(basePos), slot, variant);
            }
        }

        // (slot, variant, weight) — commons drop often, an epic almost never.
        private static readonly (int slot, int variant, float weight)[] WeaponDropTable =
        {
            (1, -1, 4f),  // Pistol (common)
            (2, -1, 4f),  // SMG (common)
            (0,  4, 3f),  // Carbine (uncommon)
            (1,  6, 3f),  // Machine Pistol (uncommon)
            (2,  8, 3f),  // PDW (uncommon)
            (0,  5, 1.4f),// Marksman (rare)
            (1,  7, 1.4f),// Magnum (rare)
            (0,  3, 1.2f),// Rare Rifle (rare)
            (2,  9, 0.5f),// Vector (epic)
        };

        private static (int slot, int variant) RollWeaponDrop()
        {
            float total = 0f;
            foreach (var e in WeaponDropTable) total += e.weight;
            float roll = Random.value * total;
            foreach (var e in WeaponDropTable)
            {
                roll -= e.weight;
                if (roll <= 0f) return (e.slot, e.variant);
            }
            return (1, -1);
        }

        private static Vector3 Scatter(Vector3 origin)
        {
            Vector2 offset = Random.insideUnitCircle * 0.6f;
            return origin + new Vector3(offset.x, 0f, offset.y);
        }

        private void RespawnServer()
        {
            _agent.Warp(_home);
            _health.ResetHealth();
            _dead = false;
            SetDeadClientRpc(false);
        }

        [ClientRpc]
        private void SetDeadClientRpc(bool dead)
        {
            foreach (var collider in _colliders) collider.enabled = !dead;
            if (visual == null) return;

            var animator = GetComponent<CharacterAnimator>();
            StopAllCoroutines();

            if (dead)
            {
                // Prefer the authored death clip; fall back to the procedural
                // topple if no clip is assigned.
                if (animator != null && animator.PlayDeath()) StartCoroutine(HideAfterDeathRoutine());
                else StartCoroutine(DeathFallRoutine());
            }
            else
            {
                animator?.ResetDeath();
                visual.gameObject.SetActive(true);
                visual.localRotation = Quaternion.identity;
                visual.localPosition = Vector3.zero;
            }
        }

        private System.Collections.IEnumerator HideAfterDeathRoutine()
        {
            yield return new WaitForSeconds(3f);
            visual.gameObject.SetActive(false);
        }

        /// Topple forward, sink slightly, then hide — reads far better than
        /// blinking out. Replaced by a real death clip when one is available.
        private System.Collections.IEnumerator DeathFallRoutine()
        {
            const float fallTime = 0.55f;
            Quaternion start = visual.localRotation;
            Quaternion end = Quaternion.Euler(88f, 0f, 0f);
            Vector3 startPos = visual.localPosition;
            Vector3 endPos = startPos + Vector3.down * 0.15f;

            for (float t = 0f; t < fallTime; t += Time.deltaTime)
            {
                float k = t / fallTime;
                // Ease-in so it accelerates like a real fall.
                visual.localRotation = Quaternion.Slerp(start, end, k * k);
                visual.localPosition = Vector3.Lerp(startPos, endPos, k * k);
                yield return null;
            }
            visual.localRotation = end;
            yield return new WaitForSeconds(1.2f);
            visual.gameObject.SetActive(false);
        }
    }

}
