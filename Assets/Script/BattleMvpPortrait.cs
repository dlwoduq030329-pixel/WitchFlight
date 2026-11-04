using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Only transforms and visual components are copied. Never spawn gameplay/network scripts for portraits.
public sealed class BattleMvpPortrait : MonoBehaviour
{
    public const int Layer = 30;
    private Animator animator;
    private Camera portraitCamera;
    private RenderTexture texture;
    private PlayableGraph graph;
    private AnimationClipPlayable idle, happy;
    private AnimationMixerPlayable mixer;
    private float praisedAt = -100f;
    private Transform upperArm, forearm, hand, head, chest;
    private CatAvatar cat;
    private BattleResultsTable table;

    public static GameObject CacheCharacter(Player player)
    {
        Animator sourceAnimator = player.GetComponentInChildren<Animator>(true);
        if (sourceAnimator == null || !sourceAnimator.isHuman || sourceAnimator.avatar == null) return null;
        var root = new GameObject("MVP character visual cache");
        root.SetActive(false);
        root.layer = Layer;
        var map = new Dictionary<Transform, Transform> { [player.transform] = root.transform };
        // Copy the entire skeleton, including inactive bones, without behaviours/colliders.
        foreach (Transform source in player.GetComponentsInChildren<Transform>(true)) CopyTransform(source, map);
        var equipment = player.GetComponentInChildren<PlayerEquipment>(true);
        foreach (Renderer source in player.GetComponentsInChildren<Renderer>(true))
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy ||
                source.GetComponentInParent<Canvas>() != null || source.GetComponentInParent<CatAvatar>() != null ||
                (equipment != null && equipment.IsFlightEquipment(source.transform))) continue;
            Renderer target;
            var copy = map[source.transform];
            if (source is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                var next = copy.gameObject.AddComponent<SkinnedMeshRenderer>();
                next.sharedMesh = skin.sharedMesh;
                var bones = new Transform[skin.bones.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = skin.bones[i] != null ? map[skin.bones[i]] : null;
                next.bones = bones;
                next.rootBone = skin.rootBone != null ? map[skin.rootBone] : null;
                next.localBounds = skin.localBounds; next.updateWhenOffscreen = true;
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) next.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                target = next;
            }
            else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                target = copy.gameObject.AddComponent<MeshRenderer>();
            }
            else continue;
            target.sharedMaterials = source.sharedMaterials;
            target.shadowCastingMode = ShadowCastingMode.Off; target.receiveShadows = false;
            target.lightProbeUsage = LightProbeUsage.Off; target.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block); target.SetPropertyBlock(block);
            for (int i = 0; i < source.sharedMaterials.Length; i++)
            { source.GetPropertyBlock(block, i); target.SetPropertyBlock(block, i); block.Clear(); }
        }
        var anim = map[sourceAnimator.transform].gameObject.AddComponent<Animator>();
        anim.avatar = sourceAnimator.avatar;
        anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        anim.updateMode = AnimatorUpdateMode.UnscaledTime;
        root.transform.localScale = player.transform.lossyScale;
        return root;
    }

    private static Transform CopyTransform(Transform source, Dictionary<Transform, Transform> map)
    {
        if (map.TryGetValue(source, out Transform copy)) return copy;
        Transform parent = CopyTransform(source.parent, map);
        copy = new GameObject(source.name).transform; copy.gameObject.layer = Layer;
        copy.SetParent(parent, false); copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation; copy.localScale = source.localScale;
        map[source] = copy;
        return copy;
    }

    public static BattleMvpPortrait Create(GameObject template, RawImage image, int slot, int catBody, int catEyes)
    {
        if (template == null || image == null) return null;
        var root = new GameObject("MVP portrait " + slot);
        root.transform.position = new Vector3(14000 + slot * 50, 14000, 14000);
        var view = root.AddComponent<BattleMvpPortrait>();
        view.table = BattleResultsTable.Default;
        GameObject model = Instantiate(template, root.transform);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        model.SetActive(true);
        view.animator = model.GetComponentInChildren<Animator>();
        view.StartPose();
        view.chest = view.animator.GetBoneTransform(HumanBodyBones.Chest);
        view.upperArm = view.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        view.forearm = view.animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        view.hand = view.animator.GetBoneTransform(HumanBodyBones.RightHand);
        view.head = view.animator.GetBoneTransform(HumanBodyBones.Head);
        view.cat = CatAvatar.Create(root.transform, catBody, catEyes, true);
        if (view.cat != null) { view.cat.SetLayer(Layer); view.cat.transform.localScale = Vector3.one * view.table.floatingCatScale; }
        var camera = new GameObject("MVP camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.SetParent(root.transform, false); view.portraitCamera = camera;
        camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << Layer; camera.nearClipPlane = .01f; camera.farClipPlane = 12;
        camera.allowHDR = false; camera.allowMSAA = false; camera.depth = -90;
        float aspect = Mathf.Max(.2f, image.rectTransform.rect.width / Mathf.Max(1, image.rectTransform.rect.height));
        int width = view.table.portraitResolution;
        view.texture = new RenderTexture(width, Mathf.Clamp(Mathf.RoundToInt(width / aspect), 128, 1536), 24, RenderTextureFormat.ARGB32);
        view.texture.Create(); camera.targetTexture = view.texture; camera.aspect = aspect;
        image.texture = view.texture; image.color = Color.white; image.raycastTarget = false;
        // Authored MVP RawImages are disabled until a character texture is available.
        image.gameObject.SetActive(true);
        image.enabled = true;
        Transform foot = view.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        float bottom = foot != null ? foot.position.y - .1f : root.transform.position.y;
        float top = view.head != null ? view.head.position.y + .55f : bottom + 2f;
        Vector3 center = new Vector3(root.transform.position.x, (bottom + top) * .5f, root.transform.position.z);
        camera.orthographicSize = Mathf.Max((top - bottom) * .57f, .75f / aspect);
        camera.transform.position = center + Vector3.forward * 5f;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
        if (view.cat != null)
        {
            Vector3 position = new Vector3(0, bottom - root.transform.position.y + view.table.floatingCatHeight, 0);
            view.cat.transform.localPosition = position;
            var floating = view.cat.gameObject.AddComponent<CatIntroFloat>();
            floating.Initialize(position, slot * 1.7f);
            floating.PlaceBeside(camera, model.GetComponentsInChildren<Renderer>(), 1, view.table.floatingCatGap);
        }
        CatIntroFloat.FrameWholeGroup(root.transform,camera);
        var light = new GameObject("MVP fill", typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(root.transform, false); light.transform.position = center + new Vector3(0, 1, 3);
        light.type = LightType.Point; light.range = 8; light.intensity = 2; light.cullingMask = 1 << Layer;
        return view;
    }

    private void StartPose()
    {
        graph = PlayableGraph.Create("MVP standing / praise"); graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        var output = AnimationPlayableOutput.Create(graph, "Character", animator); output.SetSourcePlayable(mixer);
        if (table.standingClip != null)
        {
            idle = AnimationClipPlayable.Create(graph, table.standingClip);
            graph.Connect(idle, 0, mixer, 0); mixer.SetInputWeight(0, 1);
        }
        if (table.happyClip != null)
        {
            happy = AnimationClipPlayable.Create(graph, table.happyClip);
            graph.Connect(happy, 0, mixer, 1);
        }
        graph.Play(); graph.Evaluate(0);
    }

    public void Praise()
    {
        praisedAt = Time.unscaledTime;
        if (happy.IsValid()) happy.SetTime(0);
        if (cat != null) cat.Play(CatAnimation.Cheer);
    }

    private void Update()
    {
        if (!graph.IsValid()) return;
        if (idle.IsValid() && table.standingClip != null && idle.GetTime() > table.standingClip.length)
            idle.SetTime(idle.GetTime() % Mathf.Max(.1f, table.standingClip.length));
        float elapsed = Time.unscaledTime - praisedAt;
        float weight = Mathf.Clamp01(elapsed / .2f) * Mathf.Clamp01((table.praiseSeconds - elapsed) / .25f);
        if (happy.IsValid()) { mixer.SetInputWeight(0, 1 - weight); mixer.SetInputWeight(1, weight); }
    }

    private void LateUpdate()
    {
        float elapsed = Time.unscaledTime - praisedAt;
        float weight = Mathf.Clamp01(elapsed / .2f) * Mathf.Clamp01((table.praiseSeconds - elapsed) / .25f);
        // A friendly right-hand wave until an authored happy animation is assigned.
        // Animator supplies the standing pose each frame, so these offsets never accumulate.
        if (!happy.IsValid() && weight > 0)
        {
            Vector3 outward = upperArm != null && chest != null ? Vector3.ProjectOnPlane(upperArm.position - chest.position, transform.up).normalized : transform.right;
            if (upperArm != null && forearm != null)
                upperArm.rotation = Quaternion.Slerp(upperArm.rotation,
                    Quaternion.FromToRotation(forearm.position - upperArm.position, (outward * .65f + transform.up * .7f).normalized) * upperArm.rotation, weight);
            if (forearm != null && hand != null)
                forearm.rotation = Quaternion.Slerp(forearm.rotation,
                    Quaternion.FromToRotation(hand.position - forearm.position, transform.up) * forearm.rotation, weight);
            if (hand != null) hand.localRotation *= Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 12) * 22 * weight);
            if (head != null) head.localRotation *= Quaternion.Euler(0, 0, -7 * weight);
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (portraitCamera != null) portraitCamera.targetTexture = null;
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
