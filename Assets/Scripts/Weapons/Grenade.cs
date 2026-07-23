using ClutchFPS.Core;
using UnityEngine;

namespace ClutchFPS.Weapons
{
    /// Frag grenade support. The server owns the authoritative flight and the
    /// explosion; every client runs the same deterministic step purely to draw
    /// the thing arcing through the air. Nothing here is a NetworkObject, so no
    /// network prefab registration is needed.
    public static class Grenade
    {
        public const float ThrowSpeed = 15f;
        public const float Fuse = 2.6f;
        public const float Radius = 6.5f;
        public const float MaxDamage = 115f;
        private const float Bounciness = 0.42f;

        /// One integration step of the arc, shared by server and client so the
        /// visual lands where the damage does. Returns the new position.
        public static Vector3 Step(Vector3 position, ref Vector3 velocity, float dt)
        {
            velocity += Physics.gravity * dt;
            Vector3 next = position + velocity * dt;

            if (Physics.Linecast(position, next, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                velocity = Vector3.Reflect(velocity, hit.normal) * Bounciness;
                return hit.point + hit.normal * 0.06f;
            }
            return next;
        }

        /// Damage falls off from the centre; anything with a Health in range
        /// takes it, including the thrower. Server-only.
        public static void ServerExplode(Vector3 centre, ulong attackerClientId)
        {
            var hits = Physics.OverlapSphere(centre, Radius, ~0, QueryTriggerInteraction.Ignore);
            var damaged = new System.Collections.Generic.HashSet<Health>();

            foreach (var col in hits)
            {
                var health = col.GetComponentInParent<Health>();
                if (health == null || !damaged.Add(health)) continue;

                float distance = Vector3.Distance(centre, col.transform.position);
                float falloff = Mathf.Clamp01(1f - distance / Radius);
                float damage = MaxDamage * falloff * falloff; // quadratic: edges only sting

                if (damage < 1f) continue;
                health.TakeDamage(damage, attackerClientId);

                if (health.TryGetComponent<Player.PlayerRespawn>(out var respawn))
                {
                    respawn.ServerReportDamage(centre);
                }
            }
        }

        /// Cosmetic only: a little tumbling ball that follows the same arc, then
        /// deletes itself when the fuse runs out.
        public static void SpawnVisual(Vector3 origin, Vector3 direction)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GrenadeVisual";
            go.transform.localScale = Vector3.one * 0.18f;
            Object.Destroy(go.GetComponent<Collider>()); // never blocks anything
            if (go.TryGetComponent<Renderer>(out var renderer))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                var tint = Items.Get((int)ItemType.Grenade).Tint;
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block);
            }
            var visual = go.AddComponent<GrenadeVisual>();
            visual.Launch(origin, direction * ThrowSpeed);
        }

        /// A brief flash + light where it went off, plus a distance-scaled kick
        /// to the local camera.
        public static void SpawnExplosionFx(Vector3 centre)
        {
            var go = new GameObject("GrenadeFx");
            go.transform.position = centre;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.35f);
            light.range = Radius * 2.2f;
            light.intensity = 14f;
            light.shadows = LightShadows.None;
            go.AddComponent<ExplosionFlash>();

            var camera = Camera.main;
            if (camera != null)
            {
                float distance = Vector3.Distance(camera.transform.position, centre);
                float strength = Mathf.Clamp01(1f - distance / (Radius * 3f));
                if (strength > 0f) Player.CameraShake.Add(0.9f * strength);
            }
        }
    }

    /// Client-side arc for the thrown ball.
    public class GrenadeVisual : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _life;
        private Vector3 _spin;

        public void Launch(Vector3 origin, Vector3 velocity)
        {
            transform.position = origin;
            _velocity = velocity;
            _spin = new Vector3(Random.Range(-720f, 720f), Random.Range(-720f, 720f), 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            transform.position = Grenade.Step(transform.position, ref _velocity, dt);
            transform.Rotate(_spin * dt, Space.Self);

            _life += dt;
            if (_life >= Grenade.Fuse) Destroy(gameObject);
        }
    }

    /// Fades the explosion light out and cleans up after itself.
    public class ExplosionFlash : MonoBehaviour
    {
        private Light _light;
        private float _life;
        private const float Duration = 0.45f;

        private void Awake() => _light = GetComponent<Light>();

        private void Update()
        {
            _life += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_life / Duration);
            if (_light != null) _light.intensity = 14f * k * k;
            if (_life >= Duration) Destroy(gameObject);
        }
    }
}
