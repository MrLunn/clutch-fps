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
        [SerializeField] private float sightRange = 18f;
        [SerializeField] private float attackRange = 13f;
        [SerializeField] private float eyeHeight = 1.5f;

        [Header("Combat")]
        [SerializeField] private float damage = 12f;
        [SerializeField] private float fireCooldown = 1.2f;
        [SerializeField] private float aimSpreadDegrees = 4f;
        [SerializeField] private AudioClip fireSound;
        [SerializeField] private Material tracerMaterial;

        [Header("Life")]
        [SerializeField] private float patrolRadius = 6f;
        [SerializeField] private float respawnDelay = 12f;
        [SerializeField] private Transform visual;

        private NavMeshAgent _agent;
        private Health _health;
        private Collider[] _colliders;
        private Vector3 _home;
        private float _nextFireTime;
        private float _nextTargetScan;
        private PlayerRespawn _target;
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
            if (_dead || !_agent.isOnNavMesh) return;

            if (Time.time >= _nextTargetScan)
            {
                _nextTargetScan = Time.time + 0.5f;
                _target = FindNearestPlayer();
            }

            if (_target == null)
            {
                Patrol();
                return;
            }

            Vector3 targetChest = _target.transform.position + Vector3.up * 1.2f;
            float distance = Vector3.Distance(transform.position, _target.transform.position);
            bool visible = HasLineOfSight(targetChest);

            if (visible && distance <= attackRange)
            {
                _agent.SetDestination(transform.position);
                FaceTarget(targetChest);
                if (Time.time >= _nextFireTime)
                {
                    _nextFireTime = Time.time + fireCooldown;
                    FireAt(targetChest);
                }
            }
            else if (distance <= sightRange * 1.3f)
            {
                _agent.SetDestination(_target.transform.position);
            }
            else
            {
                Patrol();
            }
        }

        private PlayerRespawn FindNearestPlayer()
        {
            PlayerRespawn nearest = null;
            float best = sightRange;
            foreach (var player in FindObjectsByType<PlayerRespawn>(FindObjectsSortMode.None))
            {
                if (player.IsDead) continue;
                // Extracted players have left the raid.
                if (player.TryGetComponent<RaidController>(out var raid) && raid.HasExtracted) continue;
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = player;
                }
            }
            return nearest;
        }

        private bool HasLineOfSight(Vector3 targetPoint)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 direction = targetPoint - eye;
            if (!Physics.Raycast(eye, direction.normalized, out RaycastHit hit,
                direction.magnitude + 0.5f, ~(1 << 6))) return false;
            return hit.collider.GetComponentInParent<PlayerRespawn>() == _target;
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
            Vector3 direction = (targetPoint - eye).normalized;
            float yaw = Random.Range(-aimSpreadDegrees, aimSpreadDegrees);
            float pitch = Random.Range(-aimSpreadDegrees, aimSpreadDegrees);
            direction = Quaternion.Euler(pitch, yaw, 0f) * direction;

            Vector3 hitPoint = eye + direction * sightRange;
            if (Physics.Raycast(eye, direction, out RaycastHit hit, sightRange, ~(1 << 6)))
            {
                hitPoint = hit.point;
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(damage, AiClientId);
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
            SetDeadClientRpc(true);
            Invoke(nameof(RespawnServer), respawnDelay);
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
