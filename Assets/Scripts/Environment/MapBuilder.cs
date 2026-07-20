using System.Collections.Generic;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Builds the raid map's static geometry from code. Deterministic, so
    /// every client constructs an identical world without networking any of
    /// it — only gameplay objects (enemies, loot, extracts) are networked.
    ///
    /// Zones are colour-coded with neon so players can navigate by sight:
    /// cyan warehouse (west), pink offices (north), amber container yard
    /// (east), with an open plaza in the middle.
    public class MapBuilder : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material officeMaterial;
        [SerializeField] private Material metalMaterial;
        [SerializeField] private Material crateMaterial;
        [SerializeField] private Material ceilingMaterial;
        [SerializeField] private Material neonCyan;
        [SerializeField] private Material neonPink;
        [SerializeField] private Material neonAmber;

        [Header("Layout")]
        [SerializeField] private float mapSize = 90f;
        [SerializeField] private float wallHeight = 6f;

        private Transform _root;

        private void Awake()
        {
            _root = new GameObject("GeneratedMap").transform;
            _root.SetParent(transform, false);

            BuildLighting();
            BuildGround();
            BuildPerimeter();
            BuildWarehouse();
            BuildOffices();
            BuildContainerYard();
            BuildPlaza();
            BuildSpawnYard();
        }

        // ---------- primitives ----------

        private GameObject Box(string name, Vector3 position, Vector3 size, Material material,
            bool collide = true, float yaw = 0f, bool tile = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            if (tile && material != null) ApplyTiling(renderer, size);
            if (!collide) Destroy(go.GetComponent<Collider>());
            return go;
        }

        /// A cube's UVs run 0..1 per face regardless of how far it is stretched,
        /// so a shared material would smear across a 90m floor. Scale the UVs
        /// per object with a property block — the texture then reads at a
        /// constant real-world size everywhere.
        private static void ApplyTiling(Renderer renderer, Vector3 size)
        {
            const float metresPerTile = 4f;
            // Flat slabs (floors, roofs) tile across their footprint; uprights
            // tile across their length and height.
            bool flat = size.y <= size.x && size.y <= size.z;
            float u = (flat ? size.x : Mathf.Max(size.x, size.z)) / metresPerTile;
            float v = (flat ? size.z : size.y) / metresPerTile;

            _block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            var st = new Vector4(Mathf.Max(u, 0.05f), Mathf.Max(v, 0.05f), 0f, 0f);
            _block.SetVector(BaseMapST, st);
            _block.SetVector(MainTexST, st);
            renderer.SetPropertyBlock(_block);
        }

        private static MaterialPropertyBlock _block;
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");

        private void Neon(string name, Vector3 position, Vector3 size, Material material, float yaw = 0f)
        {
            var go = Box(name, position, size, material, collide: false, yaw: yaw, tile: false);
            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ZoneLight(Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject("ZoneLight");
            go.transform.SetParent(_root, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        /// Four walls with a doorway gap on one side (0 = north, 1 = east,
        /// 2 = south, 3 = west), optional roof.
        private void Room(Vector3 centre, Vector2 size, int doorSide, Material wall,
            bool roof, float height)
        {
            float hw = size.x / 2f, hd = size.y / 2f;
            const float door = 4f;
            float t = 0.4f;

            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side == 0 || side == 2;
                float length = horizontal ? size.x : size.y;
                Vector3 offset = side switch
                {
                    0 => new Vector3(0f, 0f, hd),
                    1 => new Vector3(hw, 0f, 0f),
                    2 => new Vector3(0f, 0f, -hd),
                    _ => new Vector3(-hw, 0f, 0f)
                };
                Vector3 pos = centre + offset + Vector3.up * height / 2f;
                Vector3 scale = horizontal
                    ? new Vector3(length, height, t)
                    : new Vector3(t, height, length);

                if (side != doorSide)
                {
                    Box("Wall", pos, scale, wall);
                    continue;
                }
                // Split the wall into two segments to leave a doorway.
                float segment = (length - door) / 2f;
                Vector3 dir = horizontal ? Vector3.right : Vector3.forward;
                Vector3 segScale = horizontal
                    ? new Vector3(segment, height, t)
                    : new Vector3(t, height, segment);
                Box("Wall", pos + dir * (door + segment) / 2f, segScale, wall);
                Box("Wall", pos - dir * (door + segment) / 2f, segScale, wall);
            }

            if (roof)
            {
                Box("Roof", centre + Vector3.up * height, new Vector3(size.x, 0.4f, size.y), ceilingMaterial);
            }
        }

        // ---------- lighting ----------

        /// The scene ships with no lights of its own, so the whole rig lives
        /// here: a cool key light for shape and shadow, a warm bounce fill so
        /// unlit faces don't go black, and lamp posts that put readable pools
        /// of light on the ground between zones.
        private void BuildLighting()
        {
            var key = new GameObject("KeyLight");
            key.transform.SetParent(_root, false);
            key.transform.rotation = Quaternion.Euler(48f, 214f, 0f);
            var keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(0.62f, 0.72f, 0.95f);
            keyLight.intensity = 1.15f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.72f;

            var fill = new GameObject("FillLight");
            fill.transform.SetParent(_root, false);
            fill.transform.rotation = Quaternion.Euler(24f, 32f, 0f);
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(1f, 0.78f, 0.6f);
            fillLight.intensity = 0.42f;
            fillLight.shadows = LightShadows.None;

            // Lamp posts ring the open ground so the outdoor zones read at
            // night without flooding them.
            Vector3[] posts =
            {
                new(-12f, 0f, -18f), new(12f, 0f, -18f),
                new(-14f, 0f, 20f), new(14f, 0f, 14f),
                new(0f, 0f, -6f), new(-2f, 0f, 30f),
                new(20f, 0f, 24f), new(-30f, 0f, -24f), new(30f, 0f, -26f)
            };
            foreach (var post in posts) LampPost(post);
        }

        /// A pole with an emissive head and a warm point light — cheap, but it
        /// gives the eye something to read distance against.
        private void LampPost(Vector3 basePosition)
        {
            Box("LampPole", basePosition + Vector3.up * 3f,
                new Vector3(0.22f, 6f, 0.22f), metalMaterial);
            Box("LampArm", basePosition + new Vector3(0.5f, 5.9f, 0f),
                new Vector3(1.2f, 0.16f, 0.16f), metalMaterial);
            Neon("LampHead", basePosition + new Vector3(1f, 5.75f, 0f),
                new Vector3(0.7f, 0.2f, 0.5f), neonAmber);
            ZoneLight(basePosition + new Vector3(1f, 5.6f, 0f),
                new Color(1f, 0.86f, 0.66f), 3.2f, 20f);
        }

        // ---------- zones ----------

        private void BuildGround()
        {
            Box("Ground", new Vector3(0f, -0.25f, 0f),
                new Vector3(mapSize, 0.5f, mapSize), floorMaterial);
        }

        private void BuildPerimeter()
        {
            float h = wallHeight, half = mapSize / 2f;
            Box("PerimN", new Vector3(0f, h / 2f, half), new Vector3(mapSize, h, 1f), wallMaterial);
            Box("PerimS", new Vector3(0f, h / 2f, -half), new Vector3(mapSize, h, 1f), wallMaterial);
            Box("PerimE", new Vector3(half, h / 2f, 0f), new Vector3(1f, h, mapSize), wallMaterial);
            Box("PerimW", new Vector3(-half, h / 2f, 0f), new Vector3(1f, h, mapSize), wallMaterial);
        }

        /// West: a big open interior with crate stacks and a catwalk.
        private void BuildWarehouse()
        {
            Vector3 centre = new(-26f, 0f, 4f);
            Room(centre, new Vector2(30f, 34f), doorSide: 1, wallMaterial, roof: true, height: 7f);

            // Crate stacks for cover.
            var rng = new System.Random(11);
            for (int i = 0; i < 14; i++)
            {
                float x = centre.x - 12f + (float)rng.NextDouble() * 24f;
                float z = centre.z - 14f + (float)rng.NextDouble() * 28f;
                float h = 1.2f + (float)rng.NextDouble() * 1.6f;
                Box("Crate", new Vector3(x, h / 2f, z), new Vector3(2f, h, 2f), crateMaterial,
                    yaw: (float)rng.NextDouble() * 40f);
            }

            // Raised catwalk along the back wall, reachable via a ramp.
            Box("Catwalk", new Vector3(centre.x - 10f, 3f, centre.z),
                new Vector3(6f, 0.3f, 30f), metalMaterial);
            Box("CatwalkRamp", new Vector3(centre.x - 4f, 1.5f, centre.z - 13f),
                new Vector3(8f, 0.3f, 10f), metalMaterial, yaw: 0f).transform.Rotate(0f, 0f, -18f);
            Neon("NeonWarehouse", new Vector3(centre.x, 6.6f, centre.z),
                new Vector3(0.3f, 0.15f, 30f), neonCyan);
            ZoneLight(new Vector3(centre.x, 5f, centre.z + 8f), new Color(0.3f, 0.8f, 1f), 2.2f, 26f);
            ZoneLight(new Vector3(centre.x, 5f, centre.z - 8f), new Color(0.3f, 0.8f, 1f), 2.2f, 26f);
        }

        /// North: a block of small offices — tight corridors, close quarters.
        private void BuildOffices()
        {
            Vector3 origin = new(6f, 0f, 28f);
            for (int col = 0; col < 3; col++)
            {
                for (int row = 0; row < 2; row++)
                {
                    Vector3 c = origin + new Vector3(col * 13f, 0f, row * 13f);
                    Room(c, new Vector2(11f, 11f), doorSide: (col + row) % 4,
                        officeMaterial, roof: true, height: 4f);
                    Neon("NeonOffice", c + Vector3.up * 3.7f,
                        new Vector3(8f, 0.12f, 0.2f), neonPink);
                    ZoneLight(c + Vector3.up * 3f, new Color(1f, 0.35f, 0.7f), 1.4f, 12f);
                }
            }
        }

        /// East: stacked shipping containers with climbable verticality.
        private void BuildContainerYard()
        {
            var rng = new System.Random(29);
            Vector3 centre = new(28f, 0f, -4f);
            for (int i = 0; i < 12; i++)
            {
                float x = centre.x - 10f + (i % 4) * 7f;
                float z = centre.z - 14f + (i / 4) * 11f;
                int stack = rng.Next(1, 3);
                for (int s = 0; s < stack; s++)
                {
                    Box("Container", new Vector3(x, 1.4f + s * 2.8f, z),
                        new Vector3(6f, 2.8f, 2.6f), metalMaterial,
                        yaw: rng.Next(0, 2) == 0 ? 0f : 90f);
                }
                if (stack > 1)
                {
                    Neon("NeonContainer", new Vector3(x, 2.85f, z),
                        new Vector3(6.1f, 0.1f, 0.2f), neonAmber);
                }
            }
            ZoneLight(centre + Vector3.up * 6f, new Color(1f, 0.75f, 0.3f), 2.4f, 30f);
            ZoneLight(centre + new Vector3(0f, 6f, 14f), new Color(1f, 0.75f, 0.3f), 1.8f, 24f);
        }

        /// Middle: open crossroads with scattered cover — high risk, best loot.
        private void BuildPlaza()
        {
            var rng = new System.Random(47);
            for (int i = 0; i < 10; i++)
            {
                float x = -8f + (float)rng.NextDouble() * 16f;
                float z = -8f + (float)rng.NextDouble() * 16f;
                Box("PlazaCover", new Vector3(x, 0.65f, z),
                    new Vector3(2.4f, 1.3f, 1.2f), metalMaterial,
                    yaw: (float)rng.NextDouble() * 180f);
            }
            // Central pillar marks the plaza from a distance.
            Box("PlazaPillar", new Vector3(0f, 4f, 0f), new Vector3(2f, 8f, 2f), wallMaterial);
            Neon("NeonPlaza", new Vector3(0f, 8.1f, 0f), new Vector3(2.4f, 0.2f, 2.4f), neonAmber);
            ZoneLight(new Vector3(0f, 7f, 0f), new Color(1f, 0.8f, 0.4f), 2.6f, 28f);
        }

        /// South: where players enter the raid. Sparse, safe-ish, signposted.
        private void BuildSpawnYard()
        {
            Neon("NeonSpawnLine", new Vector3(0f, 0.06f, -30f),
                new Vector3(40f, 0.08f, 0.3f), neonCyan);
            for (int i = -1; i <= 1; i += 2)
            {
                Box("SpawnBlock", new Vector3(i * 12f, 1.2f, -34f),
                    new Vector3(6f, 2.4f, 3f), metalMaterial);
                Neon("NeonSpawnPost", new Vector3(i * 12f, 2.6f, -34f),
                    new Vector3(6.1f, 0.14f, 0.2f), neonPink);
            }
            ZoneLight(new Vector3(0f, 5f, -32f), new Color(0.6f, 0.8f, 1f), 1.8f, 26f);
        }
    }
}
