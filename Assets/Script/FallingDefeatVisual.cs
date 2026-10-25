using System.Collections.Generic;
using UnityEngine;
using Suimono.Core;

// Render-only articulated body: copied transforms/skins, with constrained joint particles.
// No Player, NetworkObject, Animator, Rigidbody or Collider is cloned; contacts are read-only queries.
public sealed class FallingDefeatVisual : MonoBehaviour
{
    private readonly List<Mesh> ownedMeshes = new();
    private readonly Dictionary<Material, Material> copiedMaterials = new();
    private readonly List<Renderer> bodyRenderers = new();
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private Vector3 velocity, spin;
    private float gravity, remaining;
    private bool landed;
    private FlightCombatTable settings;
    private SuimonoModule water;
    private float waterHeight, nextWaterSample, accumulator;
    private bool hasWater, underwater, sampledWater, enteredWater, isBody;
    private Vector3 previousWaterPoint, rootStart, hipStart;
    private readonly Dictionary<Transform, Transform> copiedBones = new();
    private readonly List<JointParticle> joints = new();
    private readonly List<BoneLink> links = new();
    private Transform[] primaryRig;
    private readonly List<BoneFollower> extraRigBones = new();
    private Transform sourceRoot;
    private const float StepSeconds = 1f / 60f;
    private sealed class JointParticle
    {
        public Transform bone;
        public Vector3 position, previous, restDirection;
        public Quaternion localRotation;
        public float inverseMass;
        public int parent, child = -1;
    }
    private struct BoneLink { public int a, b; public float length; }
    private struct BoneFollower { public Transform driver, bone; public Vector3 offset; public Quaternion rotationOffset; }

    public static void Spawn(Player player, FlightCombatTable settings)
    {
        var equipment = player.GetComponent<PlayerEquipment>();
        Transform broom = equipment != null && equipment.CurrentBroomObject != null ? equipment.CurrentBroomObject.transform : null;
        FallingDefeatVisual body = Create("Falling witch (visual)", player.transform, settings.deathGravity, settings.deathVisualSeconds);
        FallingDefeatVisual droppedBroom = Create("Dropped broom (visual)", player.transform, settings.broomGravity, settings.deathVisualSeconds);
        body.settings = droppedBroom.settings = settings;
        body.water = droppedBroom.water = FindFirstObjectByType<SuimonoModule>();
        body.sourceRoot = player.transform;
        body.isBody = true;
        body.velocity = player.transform.forward * 3f + Vector3.down * 2f;
        body.spin = new Vector3(25f, 8f, 15f);
        droppedBroom.velocity = player.transform.right * 2f + Vector3.down;
        droppedBroom.spin = new Vector3(90f, 40f, 120f);
        foreach (Renderer source in player.GetComponentsInChildren<Renderer>(false))
        {
            if (!source.enabled || source.forceRenderingOff || (source is not MeshRenderer && source is not SkinnedMeshRenderer)) continue;
            var destination = broom != null && (source.transform == broom || source.transform.IsChildOf(broom)) ? droppedBroom : body;
            if (destination == body && settings.limpDeathBody) body.CopyArticulated(source);
            else destination.Copy(source);
        }
        if (settings.limpDeathBody) body.BuildJoints(player);
        if (body.bodyRenderers.Count == 0) Destroy(body.gameObject);
        else if (player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority &&
            CameraManager.Instance != null)
            CameraManager.Instance.GetComponent<CameraFollow>()?.ObserveFallingDeath(player, body);
        if (droppedBroom.transform.childCount == 0) Destroy(droppedBroom.gameObject);
    }

    private static FallingDefeatVisual Create(string label, Transform source, float acceleration, float lifetime)
    {
        var root = new GameObject(label);
        root.transform.SetPositionAndRotation(source.position, source.rotation);
        root.transform.localScale = source.lossyScale;
        var view = root.AddComponent<FallingDefeatVisual>();
        view.gravity = Mathf.Max(1f, acceleration);
        view.remaining = Mathf.Max(.2f, lifetime);
        return view;
    }

    private Transform CopyBone(Transform source)
    {
        if (source == null || source == sourceRoot) return transform;
        if (copiedBones.TryGetValue(source, out Transform copy)) return copy;
        Transform parent = source.IsChildOf(sourceRoot) ? CopyBone(source.parent) : transform;
        copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        if (!source.IsChildOf(sourceRoot))
        {
            copy.SetPositionAndRotation(source.position, source.rotation);
            copy.localScale = source.lossyScale;
        }
        copiedBones.Add(source, copy);
        return copy;
    }

    private void CopyArticulated(Renderer source)
    {
        Transform node = CopyBone(source.transform);
        Renderer renderer;
        if (source is SkinnedMeshRenderer skin)
        {
            if (skin.sharedMesh == null) return;
            var copy = node.gameObject.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = skin.sharedMesh;
            Transform[] bones = skin.bones;
            for (int i = 0; i < bones.Length; i++) bones[i] = bones[i] != null ? CopyBone(bones[i]) : null;
            copy.bones = bones;
            copy.rootBone = CopyBone(skin.rootBone);
            copy.localBounds = skin.localBounds;
            copy.quality = skin.quality;
            copy.updateWhenOffscreen = true;
            for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
            renderer = copy;
        }
        else
        {
            var mesh = source.GetComponent<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) return;
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            renderer = node.gameObject.AddComponent<MeshRenderer>();
        }
        node.gameObject.layer = source.gameObject.layer;
        CopyAppearance(source, renderer);
    }

    private void BuildJoints(Player player)
    {
        // Prefer avatar mappings; the named fallback also supports the shipped Rurune rig.
        HumanBodyBones[] humanoid = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
        string[] names = { "Hips", "Spine", "Chest", "Head", "Upperarm_L", "Lowerarm_L", "Left Hand",
            "Upperarm_R", "Lowerarm_R", "Right Hand", "Upperleg_L", "Lowerleg_L", "Foot_L",
            "Upperleg_R", "Lowerleg_R", "Foot_R" };
        var roots = new List<Transform>();
        foreach (Transform source in copiedBones.Keys)
            if (source.name == "Hips") roots.Add(source);
        foreach (Animator animator in player.GetComponentsInChildren<Animator>(false))
        {
            if (!animator.isHuman) continue;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null || !copiedBones.ContainsKey(hips)) continue;
            var bones = new Transform[humanoid.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = animator.GetBoneTransform(humanoid[i]);
            AddRig(bones);
            roots.Remove(hips);
        }
        foreach (Transform hips in roots)
        {
            var bones = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                bones[i] = FindRigBone(hips, names[i]);
            AddRig(bones);
        }
        if (joints.Count == 0) return; // Unknown rig keeps the rigid falling fallback.
        rootStart = transform.position;
        hipStart = joints[0].position;
        // Initial momentum differs across limbs, then gravity/constraints freely carry them.
        Vector3 momentum = player.transform.forward * Mathf.Clamp(player.CurrentSpeed * .3f, -8f, 8f) + Vector3.down * 2f;
        for (int i = 0; i < joints.Count; i++)
        {
            Vector3 loosen = new Vector3(Mathf.Sin(i * 2.4f), Mathf.Cos(i * 1.7f), Mathf.Sin(i * .9f));
            joints[i].previous = joints[i].position - (momentum + loosen * settings.deathLimbMotion) * StepSeconds;
        }
    }

    private void AddRig(Transform[] bones)
    {
        if (bones[0] == null) return;
        if (primaryRig != null)
        {
            // The shipped character has nested clothing/body rigs. Simulating both independently
            // tears clothing away; drive matching secondary bones with the same physical pose.
            if (primaryRig[0] == bones[0]) return;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || primaryRig[i] == null || bones[i] == primaryRig[i]) continue;
                bool ancestorOfPrimary = false;
                foreach (Transform primary in primaryRig)
                    if (primary != null && primary.IsChildOf(bones[i])) { ancestorOfPrimary = true; break; }
                if (ancestorOfPrimary) continue;
                Transform driver = CopyBone(primaryRig[i]), bone = CopyBone(bones[i]);
                if (extraRigBones.Exists(item => item.bone == bone)) continue;
                extraRigBones.Add(new BoneFollower { driver = driver, bone = bone,
                    offset = driver.InverseTransformPoint(bone.position), rotationOffset = Quaternion.Inverse(driver.rotation) * bone.rotation });
            }
            return;
        }
        primaryRig = bones;
        var indices = new Dictionary<Transform, int>();
        for (int i = 0; i < bones.Length; i++)
        {
            Transform source = bones[i];
            if (source == null) continue;
            int parent = -1;
            for (Transform ancestor = source.parent; ancestor != null && parent < 0; ancestor = ancestor.parent)
                if (indices.TryGetValue(ancestor, out int found)) parent = found;
            Transform bone = CopyBone(source);
            int index = joints.Count;
            indices.Add(source, index);
            joints.Add(new JointParticle { bone = bone, position = bone.position, previous = bone.position,
                localRotation = bone.localRotation, parent = parent, inverseMass = i < 3 ? .3f : 1f });
            if (parent >= 0)
            {
                AddLink(parent, index);
                if (joints[parent].child < 0)
                {
                    joints[parent].child = index;
                    joints[parent].restDirection = joints[parent].bone.InverseTransformDirection(bone.position - joints[parent].position);
                }
            }
        }
        // A firm torso keeps the body coherent while elbow/knee chains remain loose.
        int[,] braces = { {0,2}, {0,4}, {0,7}, {4,7}, {10,13} };
        for (int i = 0; i < braces.GetLength(0); i++)
            if (bones[braces[i,0]] != null && bones[braces[i,1]] != null &&
                indices.TryGetValue(bones[braces[i,0]], out int a) && indices.TryGetValue(bones[braces[i,1]], out int b)) AddLink(a,b);
    }

    private void AddLink(int a, int b) => links.Add(new BoneLink { a = a, b = b, length = Vector3.Distance(joints[a].position, joints[b].position) });

    private static Transform FindRigBone(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            if (child.name == "Hips") continue; // Separate nested skeleton, not this rig's limb.
            Transform found = FindRigBone(child, name);
            if (found != null) return found;
        }
        return null;
    }

    internal static Vector3 LinkCorrection(Vector3 delta, float restLength)
    {
        float length = delta.magnitude;
        return length > .00001f ? delta * ((length - restLength) / length) : Vector3.zero;
    }

    private void StepBody(float dt)
    {
        foreach (JointParticle joint in joints)
        {
            Vector3 last = joint.position;
            Vector3 motion = (joint.position - joint.previous) * (underwater ? .94f : .995f);
            if (underwater) motion.y = Mathf.Max(motion.y, -settings.deathWaterSinkSpeed * dt);
            joint.position += motion + Vector3.down * gravity * (underwater ? .18f : 1f) * dt * dt;
            joint.previous = last;
        }
        for (int iteration = 0; iteration < 8; iteration++)
        {
            foreach (BoneLink link in links)
            {
                JointParticle a = joints[link.a], b = joints[link.b];
                Vector3 correction = LinkCorrection(b.position - a.position, link.length);
                float share = a.inverseMass / (a.inverseMass + b.inverseMass);
                a.position += correction * share;
                b.position -= correction * (1f - share);
            }
            if (iteration == 3 || iteration == 7)
                foreach (JointParticle joint in joints)
                    joint.position = Sweep(joint.previous, joint.position, settings.deathJointRadius);
        }
    }

    private void ApplyBodyPose()
    {
        transform.position = rootStart + joints[0].position - hipStart;
        foreach (JointParticle joint in joints) joint.bone.localRotation = joint.localRotation;
        // Parent-first rotations keep hair, fingers, clothing and attached accessories on their bones.
        foreach (JointParticle joint in joints)
        {
            joint.bone.position = joint.position;
            if (joint.child < 0) continue;
            Vector3 direction = joints[joint.child].position - joint.position;
            if (direction.sqrMagnitude > .00001f)
                joint.bone.rotation = Quaternion.FromToRotation(joint.bone.TransformDirection(joint.restDirection), direction) * joint.bone.rotation;
        }
        foreach (BoneFollower copy in extraRigBones)
            copy.bone.SetPositionAndRotation(copy.driver.TransformPoint(copy.offset), copy.driver.rotation * copy.rotationOffset);
    }

    private void Copy(Renderer source)
    {
        Mesh mesh;
        if (source is SkinnedMeshRenderer skin)
        {
            if (skin.sharedMesh == null) return;
            mesh = new Mesh { name = "Death pose snapshot" };
            skin.BakeMesh(mesh);
            ownedMeshes.Add(mesh);
        }
        else
        {
            var filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            mesh = filter.sharedMesh;
        }
        var copy = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
        copy.layer = source.gameObject.layer;
        copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        copy.transform.localScale = source.transform.lossyScale;
        copy.transform.SetParent(transform, true);
        copy.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = copy.GetComponent<MeshRenderer>();
        CopyAppearance(source, renderer);
    }

    private void CopyAppearance(Renderer source, Renderer renderer)
    {
        bodyRenderers.Add(renderer);
        Material[] materials = source.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material original = materials[i];
            if (original == null) continue;
            if (!copiedMaterials.TryGetValue(original, out Material snapshot))
            {
                snapshot = new Material(original);
                copiedMaterials.Add(original, snapshot);
            }
            materials[i] = snapshot;
        }
        // Equipment can destroy its runtime materials when the network player despawns.
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        var block = new MaterialPropertyBlock();
        source.GetPropertyBlock(block);
        renderer.SetPropertyBlock(block);
        for (int i = 0; i < renderer.sharedMaterials.Length; i++)
        {
            source.GetPropertyBlock(block, i);
            renderer.SetPropertyBlock(block, i);
        }
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0f || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { Destroy(gameObject); return; }
        UpdateWater(joints.Count > 0 ? joints[0].position : transform.position);
        if (joints.Count > 0)
        {
            accumulator = Mathf.Min(accumulator + Time.deltaTime, .1f);
            while (accumulator >= StepSeconds)
            {
                StepBody(StepSeconds);
                accumulator -= StepSeconds;
            }
            ApplyBodyPose();
            return;
        }
        if (landed) return;
        float dt = Mathf.Min(Time.deltaTime, .05f);
        velocity += Vector3.down * gravity * (underwater ? .18f : 1f) * dt;
        if (underwater)
        {
            velocity *= Mathf.Exp(-2f * dt);
            velocity.y = Mathf.Max(velocity.y, -settings.deathWaterSinkSpeed);
        }
        Vector3 desired = transform.position + velocity * dt;
        Vector3 next = Sweep(transform.position, desired, .15f);
        if ((next - desired).sqrMagnitude > .0001f) landed = true;
        transform.position = next;
        transform.Rotate(spin * dt * (underwater ? .2f : 1f), Space.Self);
    }

    private bool Solid(Collider collider)
    {
        if (collider == null || collider.isTrigger || collider.GetComponentInParent<Player>() != null ||
            collider.GetComponentInParent<MagicProjectile>() != null) return false;
        // Water has a collider for height queries. It is NOT a floor for dead bodies/brooms.
        return (water == null || collider.gameObject.layer != water.layerWaterNum) &&
            collider.GetComponentInParent<SuimonoObject>() == null;
    }

    private Vector3 Sweep(Vector3 from, Vector3 to, float radius)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < .00001f) return to;
        radius = Mathf.Max(.01f, radius);
        int count = Physics.SphereCastNonAlloc(from, radius, delta / length, hits, length + .01f, ~0, QueryTriggerInteraction.Ignore);
        // A dense mesh cluster must not hide the closest solid contact behind ignored objects.
        RaycastHit[] contacts = count == hits.Length
            ? Physics.SphereCastAll(from, radius, delta / length, length + .01f, ~0, QueryTriggerInteraction.Ignore) : hits;
        if (contacts != hits) count = contacts.Length;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!Solid(contacts[i].collider)) continue;
            nearest = Mathf.Min(nearest, contacts[i].distance);
        }
        return nearest <= length + .01f ? from + delta / length * Mathf.Max(0f, nearest - .01f) : to;
    }

    private void UpdateWater(Vector3 point)
    {
        if (water == null || !water.isActiveAndEnabled || Time.time < nextWaterSample) return;
        nextWaterSample = Time.time + .08f;
        hasWater = water.TryGetSurfaceHeight(point, out waterHeight);
        bool below = hasWater && point.y < waterHeight;
        if (isBody && !enteredWater && sampledWater && below && !underwater && previousWaterPoint.y >= waterHeight)
        {
            enteredWater = true;
            Vector3 entry = new Vector3(point.x, waterHeight + .04f, point.z);
            Camera viewer = Camera.main;
            if (water.playSounds && viewer != null && (viewer.transform.position - entry).sqrMagnitude <= 1600f)
                BattleAudio.Play(BattleSound.WaterEnter, entry);
            GameObject splash = CombatPresentation.InstantiateVfx(settings.deathWaterSplashPrefab, entry, Quaternion.identity);
            if (splash != null) Destroy(splash, 3f);
            else EmitWaterRipple(entry);
        }
        underwater = below;
        previousWaterPoint = point;
        sampledWater = true;
    }

    private void EmitWaterRipple(Vector3 point)
    {
        var library = water.suimonoModuleLibrary;
        var systems = library != null && library.fxObject != null ? library.fxObject.fxParticles : null;
        if (systems == null || systems.Length <= 1 || systems[1] == null) return;
        ParticleSystem system = systems[1];
        var main = system.main;
        if (system.particleCount >= main.maxParticles) return;
        Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Local ? system.transform :
            main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : null;
        system.Emit(new ParticleSystem.EmitParams {
            position = space != null ? space.InverseTransformPoint(point) : point,
            startSize = 4f, startLifetime = 1.5f, startColor = new Color(.8f, .95f, 1f, .8f), applyShapeToPosition = false
        }, 1);
        if (!system.isPlaying) system.Play();
    }

    public Bounds CameraBounds
    {
        get
        {
            Bounds bounds = new Bounds(transform.position, Vector3.one);
            bool found = false;
            foreach (Renderer renderer in bodyRenderers)
            {
                if (renderer == null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
        foreach (Material material in copiedMaterials.Values) if (material != null) Destroy(material);
    }
}
