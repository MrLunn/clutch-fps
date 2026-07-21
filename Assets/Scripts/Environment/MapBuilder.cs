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
        [SerializeField] private Material concreteMaterial;
        [SerializeField] private Material carpetMaterial;
        [SerializeField] private Material gravelMaterial;
        [SerializeField] private Material glassMaterial;
        [SerializeField] private Material paintMaterial;
        [SerializeField] private Material containerRed;
        [SerializeField] private Material containerBlue;
        [SerializeField] private Material containerGreen;
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
            RegisterZones();
        }

        /// Publish each zone's footprint for the HUD radar's location label.
        /// Rectangles roughly match the geometry each Build* method lays down.
        private void RegisterZones()
        {
            MapZones.Clear(gameObject.scene.name);
            MapZones.Add("WAREHOUSE", new Vector2(-26f, 4f), new Vector2(32f, 36f));
            MapZones.Add("OFFICES", new Vector2(19f, 34f), new Vector2(40f, 26f));
            MapZones.Add("CONTAINER YARD", new Vector2(28f, -4f), new Vector2(30f, 42f));
            MapZones.Add("PLAZA", new Vector2(0f, 0f), new Vector2(34f, 34f));
            MapZones.Add("STAGING AREA", new Vector2(0f, -33f), new Vector2(46f, 22f));
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

        /// A ground patch laid over the base ground so a zone reads as its own
        /// surface — concrete slab, gravel apron, painted asphalt.
        private void Surface(string name, Vector3 centre, Vector2 size, Material material)
        {
            Box(name, new Vector3(centre.x, 0.03f, centre.z),
                new Vector3(size.x, 0.06f, size.y), material, collide: false);
        }

        /// West: an industrial shed. Concrete slab, roof trusses overhead,
        /// storage racking in aisles, a roller door, and hanging cyan lamps.
        private void BuildWarehouse()
        {
            Vector3 centre = new(-26f, 0f, 4f);
            const float height = 7f;
            Room(centre, new Vector2(30f, 34f), doorSide: 1, wallMaterial, roof: true, height: height);
            Surface("WarehouseSlab", centre, new Vector2(30f, 34f), concreteMaterial);

            // Roof trusses — the strongest cue that you are indoors.
            for (int i = -2; i <= 2; i++)
            {
                float z = centre.z + i * 7f;
                Box("Truss", new Vector3(centre.x, height - 0.5f, z),
                    new Vector3(29f, 0.35f, 0.35f), metalMaterial);
                Box("TrussBraceL", new Vector3(centre.x - 7f, height - 1.1f, z),
                    new Vector3(14f, 0.2f, 0.2f), metalMaterial);
            }

            // Racking in two aisles: uprights carrying shelf decks, pallets on
            // the lower level. Reads as storage, and gives layered cover.
            for (int rack = 0; rack < 2; rack++)
            {
                float x = centre.x - 8f + rack * 12f;
                for (int bay = -2; bay <= 2; bay++)
                {
                    float z = centre.z + bay * 6f;
                    Box("RackUpright", new Vector3(x - 1.3f, 2.5f, z),
                        new Vector3(0.25f, 5f, 0.25f), metalMaterial);
                    Box("RackUpright", new Vector3(x + 1.3f, 2.5f, z),
                        new Vector3(0.25f, 5f, 0.25f), metalMaterial);
                }
                for (int deck = 0; deck < 3; deck++)
                {
                    Box("RackDeck", new Vector3(x, 1.6f + deck * 1.7f, centre.z),
                        new Vector3(3f, 0.12f, 24f), metalMaterial);
                }
            }

            // Palletised stock in the aisles, plus loose crates for cover.
            var rng = new System.Random(11);
            for (int i = 0; i < 16; i++)
            {
                float x = centre.x - 12f + (float)rng.NextDouble() * 24f;
                float z = centre.z - 14f + (float)rng.NextDouble() * 28f;
                float h = 1.1f + (float)rng.NextDouble() * 1.4f;
                Box("Pallet", new Vector3(x, 0.1f, z), new Vector3(2.4f, 0.2f, 2.4f), crateMaterial);
                Box("Crate", new Vector3(x, 0.2f + h / 2f, z), new Vector3(2f, h, 2f), crateMaterial,
                    yaw: (float)rng.NextDouble() * 40f);
            }

            // Roller door on the open east face.
            Box("RollerDoorFrame", new Vector3(centre.x + 15f, 5.2f, centre.z),
                new Vector3(0.5f, 0.6f, 5f), metalMaterial);
            Neon("RollerDoorLight", new Vector3(centre.x + 14.4f, 5.6f, centre.z),
                new Vector3(0.2f, 0.15f, 4.6f), neonCyan);

            // Raised catwalk along the back wall, reachable via a ramp.
            Box("Catwalk", new Vector3(centre.x - 13f, 3f, centre.z),
                new Vector3(3.5f, 0.3f, 30f), metalMaterial);
            Box("CatwalkRail", new Vector3(centre.x - 11.4f, 3.6f, centre.z),
                new Vector3(0.12f, 1.2f, 30f), metalMaterial);
            Box("CatwalkRamp", new Vector3(centre.x - 8f, 1.5f, centre.z - 13f),
                new Vector3(8f, 0.3f, 10f), metalMaterial).transform.Rotate(0f, 0f, -18f);

            // Hanging shop lamps down the centre line.
            for (int i = -1; i <= 1; i++)
            {
                float z = centre.z + i * 11f;
                Box("LampChain", new Vector3(centre.x, height - 1.1f, z),
                    new Vector3(0.08f, 1.4f, 0.08f), metalMaterial);
                Neon("ShopLamp", new Vector3(centre.x, height - 1.9f, z),
                    new Vector3(2.2f, 0.25f, 0.9f), neonCyan);
                ZoneLight(new Vector3(centre.x, height - 2.2f, z),
                    new Color(0.45f, 0.85f, 1f), 3f, 20f);
            }
        }

        /// North: an office block. Carpet, glazing, desks and partitions —
        /// the only zone that feels like an interior people worked in.
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
                    Surface("OfficeCarpet", c, new Vector2(10.6f, 10.6f), carpetMaterial);

                    // Glazing band on the two outward faces.
                    Box("Window", c + new Vector3(0f, 2.6f, 5.5f),
                        new Vector3(7f, 1.4f, 0.12f), glassMaterial, collide: false);
                    Box("Window", c + new Vector3(5.5f, 2.6f, 0f),
                        new Vector3(0.12f, 1.4f, 7f), glassMaterial, collide: false);

                    // Desk, chair and a partition — waist-high cover indoors.
                    Vector3 desk = c + new Vector3(-2.4f, 0f, 2f);
                    Box("DeskTop", desk + Vector3.up * 0.76f,
                        new Vector3(3f, 0.1f, 1.4f), crateMaterial);
                    Box("DeskLeg", desk + new Vector3(-1.3f, 0.38f, 0f),
                        new Vector3(0.12f, 0.76f, 1.3f), metalMaterial);
                    Box("DeskLeg", desk + new Vector3(1.3f, 0.38f, 0f),
                        new Vector3(0.12f, 0.76f, 1.3f), metalMaterial);
                    Box("Chair", desk + new Vector3(0f, 0.45f, -1.2f),
                        new Vector3(0.6f, 0.9f, 0.6f), metalMaterial);
                    Box("Partition", c + new Vector3(2.2f, 0.8f, -1f),
                        new Vector3(0.15f, 1.6f, 5f), officeMaterial);
                    Box("Cabinet", c + new Vector3(4.4f, 0.9f, -3.6f),
                        new Vector3(1.2f, 1.8f, 0.6f), metalMaterial);

                    // Ceiling panel light instead of a bare strip.
                    Neon("CeilingPanel", c + Vector3.up * 3.82f,
                        new Vector3(4f, 0.1f, 1.2f), neonPink);
                    ZoneLight(c + Vector3.up * 3.4f, new Color(1f, 0.45f, 0.75f), 2.2f, 13f);
                }
            }
        }

        /// East: an outdoor yard. Gravel underfoot, colour-coded containers in
        /// tidy lanes, a crane gantry and floodlight masts overhead.
        private void BuildContainerYard()
        {
            var rng = new System.Random(29);
            Vector3 centre = new(28f, 0f, -4f);
            Surface("YardGravel", centre, new Vector2(28f, 40f), gravelMaterial);

            Material[] palette = { containerRed, containerBlue, containerGreen };
            for (int i = 0; i < 12; i++)
            {
                float x = centre.x - 10f + (i % 4) * 7f;
                float z = centre.z - 14f + (i / 4) * 11f;
                int stack = rng.Next(1, 3);
                for (int s = 0; s < stack; s++)
                {
                    var paint = palette[rng.Next(palette.Length)];
                    float yaw = rng.Next(0, 2) == 0 ? 0f : 90f;
                    float y = 1.4f + s * 2.8f;
                    Box("Container", new Vector3(x, y, z), new Vector3(6f, 2.8f, 2.6f), paint, yaw: yaw);
                    // Door end, slightly proud so the container has a front.
                    Vector3 end = Quaternion.Euler(0f, yaw, 0f) * new Vector3(3.05f, 0f, 0f);
                    Box("ContainerDoors", new Vector3(x, y, z) + end,
                        new Vector3(0.15f, 2.4f, 2.3f), metalMaterial, collide: false, yaw: yaw);
                }
                if (stack > 1)
                {
                    Neon("NeonContainer", new Vector3(x, 2.85f, z),
                        new Vector3(6.1f, 0.1f, 0.2f), neonAmber);
                }
            }

            // Gantry crane straddling the lanes — the yard's landmark.
            for (int side = -1; side <= 1; side += 2)
            {
                Box("GantryLeg", new Vector3(centre.x + side * 12f, 5f, centre.z + 6f),
                    new Vector3(0.8f, 10f, 0.8f), metalMaterial);
            }
            Box("GantryBeam", new Vector3(centre.x, 10.2f, centre.z + 6f),
                new Vector3(25f, 0.9f, 1.2f), metalMaterial);
            Neon("GantryStrip", new Vector3(centre.x, 9.6f, centre.z + 6f),
                new Vector3(24f, 0.15f, 0.25f), neonAmber);

            // Floodlight masts.
            foreach (var offset in new[] { new Vector3(-11f, 0f, -16f), new Vector3(11f, 0f, 15f) })
            {
                Vector3 basePosition = centre + offset;
                Box("Mast", basePosition + Vector3.up * 4.5f,
                    new Vector3(0.3f, 9f, 0.3f), metalMaterial);
                Neon("Floodlight", basePosition + Vector3.up * 9f,
                    new Vector3(1.6f, 0.5f, 0.6f), neonAmber);
                ZoneLight(basePosition + Vector3.up * 8.6f,
                    new Color(1f, 0.8f, 0.45f), 4f, 30f);
            }
        }

        /// Middle: a paved crossroads. Road markings, concrete barriers and a
        /// kiosk — open enough to be dangerous, marked enough to navigate by.
        private void BuildPlaza()
        {
            var rng = new System.Random(47);
            Surface("PlazaPaving", Vector3.zero, new Vector2(34f, 34f), concreteMaterial);

            // Painted crossroads through the middle.
            for (int i = -7; i <= 7; i++)
            {
                if (i == 0) continue;
                Box("RoadDash", new Vector3(i * 2.2f, 0.07f, 0f),
                    new Vector3(1.4f, 0.04f, 0.25f), paintMaterial, collide: false);
                Box("RoadDash", new Vector3(0f, 0.07f, i * 2.2f),
                    new Vector3(0.25f, 0.04f, 1.4f), paintMaterial, collide: false);
            }

            // Jersey barriers angled around the open ground.
            for (int i = 0; i < 10; i++)
            {
                float x = -12f + (float)rng.NextDouble() * 24f;
                float z = -12f + (float)rng.NextDouble() * 24f;
                if (Mathf.Abs(x) < 4f && Mathf.Abs(z) < 4f) continue;
                Box("Barrier", new Vector3(x, 0.55f, z),
                    new Vector3(3.2f, 1.1f, 0.7f), concreteMaterial,
                    yaw: (float)rng.NextDouble() * 180f);
                Box("BarrierStripe", new Vector3(x, 1.06f, z),
                    new Vector3(3.2f, 0.12f, 0.72f), paintMaterial,
                    collide: false, yaw: (float)rng.NextDouble() * 180f);
            }

            // Kiosk: a small hard structure worth fighting over.
            Vector3 kiosk = new(9f, 0f, -9f);
            Room(kiosk, new Vector2(6f, 6f), doorSide: 3, wallMaterial, roof: true, height: 3f);
            Neon("KioskSign", kiosk + new Vector3(0f, 3.3f, 3f),
                new Vector3(4f, 0.6f, 0.15f), neonPink);
            ZoneLight(kiosk + Vector3.up * 3.6f, new Color(1f, 0.4f, 0.7f), 2f, 12f);

            // Central pillar marks the plaza from a distance.
            Box("PlazaPillar", new Vector3(0f, 4f, 0f), new Vector3(2f, 8f, 2f), concreteMaterial);
            for (int ring = 0; ring < 3; ring++)
            {
                Neon("PillarRing", new Vector3(0f, 2f + ring * 2.4f, 0f),
                    new Vector3(2.3f, 0.18f, 2.3f), ring == 1 ? neonPink : neonAmber);
            }
            Neon("NeonPlaza", new Vector3(0f, 8.1f, 0f), new Vector3(2.4f, 0.2f, 2.4f), neonAmber);
            ZoneLight(new Vector3(0f, 7f, 0f), new Color(1f, 0.8f, 0.4f), 3f, 30f);
        }

        /// South: the staging area you deploy from. Dirt, sandbags and tents —
        /// temporary and soft where every other zone is hard-edged.
        private void BuildSpawnYard()
        {
            Surface("SpawnDirt", new Vector3(0f, 0f, -32f), new Vector2(46f, 20f), gravelMaterial);

            Neon("NeonSpawnLine", new Vector3(0f, 0.09f, -28f),
                new Vector3(40f, 0.08f, 0.3f), neonCyan);

            // Sandbag emplacements flanking the deploy line.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 wallBase = new(side * 14f, 0f, -28f);
                for (int course = 0; course < 3; course++)
                {
                    int count = 7 - course;
                    for (int bag = 0; bag < count; bag++)
                    {
                        float offset = (bag - (count - 1) / 2f) * 0.95f;
                        Box("Sandbag", wallBase + new Vector3(offset, 0.22f + course * 0.42f, 0f),
                            new Vector3(0.9f, 0.42f, 0.55f), gravelMaterial,
                            yaw: course % 2 == 0 ? 0f : 8f);
                    }
                }
            }

            // Supply tents: pitched roofs on a low frame.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 tent = new(side * 8f, 0f, -35f);
                Box("TentFloor", tent + Vector3.up * 0.05f,
                    new Vector3(7f, 0.1f, 5f), crateMaterial);
                Box("TentRoofL", tent + new Vector3(-1.6f, 2f, 0f),
                    new Vector3(4.2f, 0.15f, 5.4f), officeMaterial).transform.Rotate(0f, 0f, -34f);
                Box("TentRoofR", tent + new Vector3(1.6f, 2f, 0f),
                    new Vector3(4.2f, 0.15f, 5.4f), officeMaterial).transform.Rotate(0f, 0f, 34f);
                Box("TentPost", tent + new Vector3(0f, 1.4f, -2.4f),
                    new Vector3(0.16f, 2.8f, 0.16f), metalMaterial);
                Box("TentPost", tent + new Vector3(0f, 1.4f, 2.4f),
                    new Vector3(0.16f, 2.8f, 0.16f), metalMaterial);
                Box("SupplyCrate", tent + new Vector3(2.2f, 0.6f, 1.4f),
                    new Vector3(1.6f, 1.1f, 1.2f), crateMaterial);
                Neon("TentLamp", tent + new Vector3(0f, 2.2f, 0f),
                    new Vector3(1.4f, 0.16f, 0.4f), neonPink);
                ZoneLight(tent + Vector3.up * 2f, new Color(0.7f, 0.6f, 1f), 2f, 14f);
            }

            ZoneLight(new Vector3(0f, 5f, -30f), new Color(0.6f, 0.8f, 1f), 2.2f, 28f);
        }
    }
}
