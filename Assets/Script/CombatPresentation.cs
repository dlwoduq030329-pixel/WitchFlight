using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Local presentation only. Damage, stealth and projectile hits remain host-authoritative.
[DisallowMultipleComponent]
public sealed class CombatPresentation : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float deathFadeSeconds = 2f;
    [SerializeField, Min(0.01f)] private float hitFlashSeconds = 0.18f;

    [Header("Status effect world VFX (visible to all viewers; concealed by smoke)")]
    [Tooltip("상태이상에 걸린 이 캐릭터에게 붙이는 외형 VFX입니다. 피해자의 화면 UI는 BattleHud > Status Effect Views에서 별도로 연결하세요.")]
    [SerializeField] private StatusEffectVfxBinding[] statusEffectVfxViews = {
        new StatusEffectVfxBinding { status = StatusEffectType.Binding },
        new StatusEffectVfxBinding { status = StatusEffectType.Slow },
        new StatusEffectVfxBinding { status = StatusEffectType.HitStun },
        new StatusEffectVfxBinding { status = StatusEffectType.Burn },
        new StatusEffectVfxBinding { status = StatusEffectType.Healing }
    };

    [System.Serializable]
    private sealed class StatusEffectVfxBinding
    {
        public StatusEffectType status = StatusEffectType.Binding;
        [Tooltip("외형 연출 프리팹. NetworkObject를 포함하지 않아야 합니다. 지속되는 효과는 Loop로 제작하세요. 비우면 생성하지 않습니다.")]
        public GameObject vfxPrefab;
        [Tooltip("피격 캐릭터의 몸/머리 등 부착 위치. 비우면 이 캐릭터의 Transform을 사용합니다.")]
        public Transform anchor;
        public Vector3 localOffset = Vector3.up;
        public Vector3 localEulerAngles;
        [Min(0.01f)] public float scale = 1f;
        [System.NonSerialized] public GameObject instance;
        [System.NonSerialized] public bool spawnAttempted;
    }

    private sealed class Visual
    {
        public Renderer renderer;
        public bool authoredEnabled;
        public bool authoredForceRenderingOff;
        public ShadowCastingMode authoredShadows;
        public Material[] originals;
        public Material[] fading;
        public Color[] colors;
        public MaterialPropertyBlock[] originalBlocks;
    }

    private readonly List<Visual> visuals = new();
    private MaterialPropertyBlock block;
    private Player owner;
    private Outline enemyOutline;
    private bool initialized;
    private int lastHit;
    private int lastDeath;
    private int lastParry;
    [Header("Parry directional VFX")]
    [Tooltip("직접 만든 반구 VFX. 볼록한 방향을 +Z로 제작하세요. 패링 성공 시 투사체가 날아온 쪽을 향합니다. 비우면 생성하지 않습니다.")]
    [SerializeField] private GameObject parryShieldVfxPrefab;
    [SerializeField, Min(0.05f)] private float parryShieldDuration = 0.35f;
    [Tooltip("몸 중심(LockAimPoint)에서 투사체가 날아온 방향으로 띄울 거리입니다.")]
    [SerializeField, Min(0f)] private float parryShieldForwardOffset;
    private CombatMagicVisual channelVisual;
    private GameObject lockCompleteVfx;
    private Material lockCompleteMaterial;
    private MagicType lockCompleteMagic;
    private float lockCompleteExpiresAt;
    private MagicType visualChannel;
    private static MagicStatTable cachedMagicTable;
    private float hitFlashUntil;
    private bool wasFlashing;
    private float deathStarted = -1f;
    private bool visibilityInitialized, lastSmokeHidden, lastFadeHidden;

    public static bool MenuOpen => BattleHud.MenuOpen;

    private void Awake()
    {
        // Unity native objects cannot be created from MonoBehaviour field initializers.
        block = new MaterialPropertyBlock();
        owner = GetComponent<Player>();
        enemyOutline = GetComponent<Outline>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;
            Material[] materials = renderer.sharedMaterials;
            var visual = new Visual
            {
                renderer = renderer,
                authoredEnabled = renderer.enabled,
                authoredForceRenderingOff = renderer.forceRenderingOff,
                authoredShadows = renderer.shadowCastingMode,
                originals = materials,
                colors = new Color[materials.Length],
                originalBlocks = new MaterialPropertyBlock[materials.Length]
            };
            for (int i = 0; i < materials.Length; i++)
            {
                visual.colors[i] = ReadColor(materials[i]);
                visual.originalBlocks[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(visual.originalBlocks[i], i);
                if (visual.originalBlocks[i].HasColor("_BaseColor"))
                    visual.colors[i] = visual.originalBlocks[i].GetColor("_BaseColor");
                else if (visual.originalBlocks[i].HasColor("_Color"))
                    visual.colors[i] = visual.originalBlocks[i].GetColor("_Color");
            }
            visuals.Add(visual);
        }
    }

    // Called after PlayerAppearance writes the per-material property block.
    public void SetAppearanceColor(Renderer renderer, int materialIndex, Color color)
    {
        foreach (Visual visual in visuals)
        {
            if (visual.renderer != renderer || materialIndex < 0 || materialIndex >= visual.colors.Length)
                continue;
            visual.colors[materialIndex] = color;
            renderer.GetPropertyBlock(visual.originalBlocks[materialIndex], materialIndex);
            return;
        }
    }

    // Layer tint (e.g. iris _Color2nd) is not the base color used for hit flashes.
    // Modify only this baseline property, never capture an in-progress flash as the base.
    public void SetAppearanceLayerColor(Renderer renderer, int materialIndex, string property, Color color)
    {
        foreach (Visual visual in visuals)
        {
            if (visual.renderer != renderer || materialIndex < 0 || materialIndex >= visual.colors.Length)
                continue;
            visual.originalBlocks[materialIndex].SetColor(property, color);
            return;
        }
    }

    // Preserve the selected eye texture when hit/death presentation restores the property block.
    public void SetAppearanceLayerTexture(Renderer renderer, int materialIndex, string property, Texture texture)
    {
        foreach (Visual visual in visuals)
        {
            if (visual.renderer != renderer || materialIndex < 0 || materialIndex >= visual.colors.Length)
                continue;
            visual.originalBlocks[materialIndex].SetTexture(property, texture);
            return;
        }
    }

    private void LateUpdate()
    {
        // This is viewer-local, never a replicated enable flag: each client sees only its enemy.
        UpdateEnemyOutline();
        UpdateLockCompleteVfx();
        UpdateStatusEffectVfx();
        if (owner == null || owner.Object == null || !owner.Object.IsValid)
            return;

        if (!initialized)
        {
            lastHit = owner.HitSequence;
            lastDeath = owner.DeathSequence;
            lastParry = owner.ParrySequence;
            initialized = true;
            if (!owner.IsAlive && owner.DeathSequence > 0)
                StartDeathFade();
        }

        if (lastHit != owner.HitSequence)
        {
            lastHit = owner.HitSequence;
            hitFlashUntil = Time.unscaledTime + hitFlashSeconds;
            if (!owner.IsHiddenBySmokeFor(Player.LocalPlayer))
                BattleHud.ShowDamage(owner.LockAimPoint, owner.LastReceivedDamage);

        }
        if (lastDeath != owner.DeathSequence)
        {
            lastDeath = owner.DeathSequence;
            StartDeathFade();
        }
        if (lastParry != owner.ParrySequence)
        {
            lastParry = owner.ParrySequence;
            CombatMagicVisual.Shield(owner, parryShieldVfxPrefab, parryShieldDuration, owner.ParryFacingDirection, parryShieldForwardOffset);
        }
        UpdateChannelVisual();
        UpdateMaterials();
        UpdateSmokeVisibility();
    }

    private bool CanShowEnemyOutline()
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return false;
        Player local = Player.LocalPlayer;
        return owner != null && owner.Object != null && owner.Object.IsValid && owner.IsAlive &&
            local != null && local.Object != null && local.Object.IsValid && local.IsAlive &&
            owner != local && !owner.Object.HasInputAuthority && owner.Runner == local.Runner &&
            owner.TeamIndex > 0 && local.TeamIndex > 0 && owner.TeamIndex != local.TeamIndex &&
            !owner.IsHiddenBySmokeFor(local);
    }

    private void UpdateEnemyOutline()
    {
        if (enemyOutline == null) return;
        bool visible = CanShowEnemyOutline();
        // QuickOutline changes materials on enable/disable; do that only on actual state changes.
        if (enemyOutline.enabled != visible) enemyOutline.enabled = visible;
    }

    private void UpdateSmokeVisibility()
    {
        bool hidden = owner.IsHiddenBySmokeFor(Player.LocalPlayer);
        bool faded = deathStarted >= 0f && Time.unscaledTime - deathStarted >= deathFadeSeconds;
        if (visibilityInitialized && hidden == lastSmokeHidden && faded == lastFadeHidden) return;
        visibilityInitialized = true;
        lastSmokeHidden = hidden;
        lastFadeHidden = faded;
        foreach (Visual visual in visuals)
        {
            if (visual.renderer == null) continue;
            // Never touch colliders/layers/GameObjects or shared customization materials.
            visual.renderer.forceRenderingOff = visual.authoredForceRenderingOff || hidden || faded;
            visual.renderer.shadowCastingMode = hidden ? ShadowCastingMode.Off : visual.authoredShadows;
        }
    }

    private void OnDisable()
    {
        ClearLockCompleteVfx();
        ClearStatusEffectVfx();
        if (enemyOutline != null && enemyOutline.enabled) enemyOutline.enabled = false;
        visibilityInitialized = false;
        foreach (Visual visual in visuals)
        {
            if (visual.renderer == null) continue;
            visual.renderer.forceRenderingOff = visual.authoredForceRenderingOff;
            visual.renderer.shadowCastingMode = visual.authoredShadows;
        }
    }

    private void StartDeathFade()
    {
        deathStarted = Time.unscaledTime;
        foreach (Visual visual in visuals)
        {
            if (visual.renderer == null || visual.fading != null)
                continue;
            visual.fading = new Material[visual.originals.Length];
            for (int i = 0; i < visual.originals.Length; i++)
                visual.fading[i] = CreateFadeMaterial(visual.originals[i]);
            visual.renderer.sharedMaterials = visual.fading;
        }
    }

    private void UpdateMaterials()
    {
        bool dying = deathStarted >= 0f;
        float opacity = dying ? 1f - Mathf.Clamp01((Time.unscaledTime - deathStarted) / deathFadeSeconds) : 1f;
        float flash = Mathf.Clamp01((hitFlashUntil - Time.unscaledTime) / hitFlashSeconds);
        if (!dying && flash <= 0f && !wasFlashing)
            return;
        wasFlashing = flash > 0f;
        foreach (Visual visual in visuals)
        {
            if (visual.renderer == null)
                continue;
            if (dying && opacity <= 0f)
                visual.renderer.forceRenderingOff = true;
            for (int i = 0; i < visual.colors.Length; i++)
            {
                if (!dying && flash <= 0f)
                {
                    visual.renderer.SetPropertyBlock(visual.originalBlocks[i], i);
                    continue;
                }
                Color color = Color.Lerp(visual.colors[i], Color.red, flash * 0.9f);
                color.a *= opacity;
                visual.renderer.GetPropertyBlock(block, i);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                visual.renderer.SetPropertyBlock(block, i);
            }
        }
    }

    public static MagicStatEntry Stats(MagicType magic)
    {
        if (cachedMagicTable == null) cachedMagicTable = Resources.Load<MagicStatTable>("MagicStatTable");
        return cachedMagicTable != null ? cachedMagicTable.GetStats(magic) : MagicStatTable.DefaultEntry(magic);
    }

    private MagicStatEntry OwnerStats(MagicType magic)
    {
        return owner != null && owner.MagicTable != null ? owner.MagicTable.GetStats(magic) : Stats(magic);
    }

    private void UpdateStatusEffectVfx()
    {
        if (statusEffectVfxViews == null) return;
        bool gameplay = owner != null && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        foreach (StatusEffectVfxBinding view in statusEffectVfxViews)
        {
            if (view == null) continue;
            if (!gameplay || !owner.HasStatusEffect(view.status))
            {
                ClearStatusEffectVfxView(view);
                continue;
            }
            if (!view.spawnAttempted && view.vfxPrefab != null)
            {
                view.spawnAttempted = true;
                Transform anchor = view.anchor != null ? view.anchor : transform;
                view.instance = InstantiateVfx(view.vfxPrefab, anchor.position, anchor.rotation, anchor);
                if (view.instance != null)
                {
                    view.instance.transform.localPosition = view.localOffset;
                    view.instance.transform.localRotation = Quaternion.Euler(view.localEulerAngles);
                    view.instance.transform.localScale *= Mathf.Max(0.01f, view.scale);
                }
            }
            if (view.instance == null) continue;
            bool visible = !owner.IsHiddenBySmokeFor(Player.LocalPlayer);
            if (view.instance.activeSelf != visible) view.instance.SetActive(visible);
        }
    }

    private static void ClearStatusEffectVfxView(StatusEffectVfxBinding view)
    {
        if (view.instance != null)
        {
            view.instance.SetActive(false);
            Destroy(view.instance);
        }
        view.instance = null;
        view.spawnAttempted = false;
    }

    private void ClearStatusEffectVfx()
    {
        if (statusEffectVfxViews == null) return;
        foreach (StatusEffectVfxBinding view in statusEffectVfxViews)
            if (view != null) ClearStatusEffectVfxView(view);
    }

    public void ShowLockComplete(MagicType magic)
    {
        ClearLockCompleteVfx();
        if (!isActiveAndEnabled || owner == null || owner.Object == null || !owner.Object.IsValid || !owner.IsAlive)
            return;
        MagicStatEntry stats = OwnerStats(magic);
        if (!stats.requiresTarget) return;
        Transform anchor = owner.MagicCastAnchor;
        lockCompleteVfx = InstantiateVfx(stats.lockOnCompleteVfxPrefab, anchor.position, anchor.rotation, anchor);
        if (lockCompleteVfx == null)
        {
            lockCompleteVfx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = lockCompleteVfx.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            lockCompleteVfx.transform.SetParent(anchor, false);
            lockCompleteVfx.transform.localScale = Vector3.one * 0.25f;
            lockCompleteMaterial = CreateEffectMaterial(MagicColor(magic));
            var renderer = lockCompleteVfx.GetComponent<Renderer>();
            renderer.sharedMaterial = lockCompleteMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        lockCompleteVfx.name = "Lock-on complete (wand)";
        lockCompleteMagic = magic;
        lockCompleteExpiresAt = Time.time + Mathf.Max(0.05f, stats.vfxLifetime);
        // RPCs can arrive before the lock snapshot. Do not gate this event on IsFullyLocked.
        lockCompleteVfx.SetActive(!owner.IsHiddenBySmokeFor(Player.LocalPlayer));
    }

    private void UpdateLockCompleteVfx()
    {
        if (lockCompleteVfx == null) return;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.IsAlive ||
            owner.GetSelectedMagic() != lockCompleteMagic || Time.time >= lockCompleteExpiresAt ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        {
            ClearLockCompleteVfx();
            return;
        }
        bool visible = !owner.IsHiddenBySmokeFor(Player.LocalPlayer);
        if (lockCompleteVfx.activeSelf != visible) lockCompleteVfx.SetActive(visible);
    }

    public void ClearLockCompleteVfx()
    {
        if (lockCompleteVfx != null)
        {
            lockCompleteVfx.SetActive(false);
            Destroy(lockCompleteVfx);
        }
        if (lockCompleteMaterial != null) Destroy(lockCompleteMaterial);
        lockCompleteVfx = null;
        lockCompleteMaterial = null;
        lockCompleteMagic = MagicType.None;
    }

    // Author VFX here / in MagicStatTable. Prefabs must be visual-only, without NetworkObject.
    public static GameObject InstantiateVfx(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null) return null;
        if (prefab.GetComponentInChildren<Fusion.NetworkObject>(true) != null)
        {
            Debug.LogWarning("Magic VFX must not contain a NetworkObject: " + prefab.name);
            return null;
        }
        GameObject view = Instantiate(prefab, position, rotation, parent);
        foreach (Collider collider in view.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in view.GetComponentsInChildren<Rigidbody>(true))
        { body.detectCollisions = false; body.isKinematic = true; body.useGravity = false; }
        foreach (ParticleSystem particles in view.GetComponentsInChildren<ParticleSystem>(true))
        { var collision = particles.collision; collision.enabled = false; var trigger = particles.trigger; trigger.enabled = false; }
        view.SetActive(true);
        return view;
    }

    public void ShowCast(MagicType magic, Vector3 origin, Vector3 end)
    {
        ClearLockCompleteVfx();
        MagicStatEntry stats = OwnerStats(magic);
        // Utility effects have their own fallback; the optional cast effect is additive.
        bool utility = stats.effect == MagicEffectKind.Flare || stats.effect == MagicEffectKind.Smoke;
        if (!utility || stats.castVfxPrefab != null) ShowCastVfx(stats, origin);
        if (stats.effect == MagicEffectKind.Flare)
        {
            // end is the authoritative rear direction for this self-cast, not a hit point.
            UtilityMagicVisual.PlayFlare(owner, origin, end, stats);
            return;
        }
        if (stats.effect == MagicEffectKind.Smoke) return; // Persistent BattleFlag snapshot owns its VFX.
        if (stats.projectileSpeed > 0f || stats.IsChanneled || stats.effect == MagicEffectKind.Healing ||
            stats.effect == MagicEffectKind.DelayedArea || stats.effect == MagicEffectKind.PursuitSummon) return;
        if (stats.beamVfxPrefab == null && stats.beamMaterial == null)
            CombatTransientEffect.PlayBeam(origin, end, MagicColor(magic));
        else
        {
            CombatMagicVisual beam = CombatMagicVisual.Beam(magic, stats.beamVfxPrefab, stats.beamMaterial);
            beam.SetEndpoints(origin, end);
            Destroy(beam.gameObject, 0.2f);
        }
        ShowImpact(end, magic);
    }

    private void ShowCastVfx(MagicStatEntry stats, Vector3 origin)
    {
        GameObject view = InstantiateVfx(stats.castVfxPrefab, origin, transform.rotation);
        if (view != null) Destroy(view, Mathf.Max(0.05f, stats.vfxLifetime));
        else SpawnPulse(origin, MagicColor(stats.magic), stats.effect == MagicEffectKind.Healing ? 2f : 0.7f, 0.25f);
    }

    private void UpdateChannelVisual()
    {
        MagicType current = owner.IsAlive && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive
            ? owner.ChannelMagic : MagicType.None;
        if (visualChannel != current)
        {
            if (channelVisual != null) Destroy(channelVisual.gameObject);
            visualChannel = current;
            channelVisual = null;
            if (current != MagicType.None)
            {
                ClearLockCompleteVfx();
                MagicStatEntry stats = OwnerStats(current);
                ShowCastVfx(stats, owner.MagicCastPosition);
                channelVisual = CombatMagicVisual.Beam(current, stats.beamVfxPrefab, stats.beamMaterial);
            }
        }
        if (channelVisual == null) return;
        Vector3 end = owner.ChannelEnd;
        if (current == MagicType.Curse && owner.Runner.TryFindObject(owner.ChannelTargetId, out Fusion.NetworkObject target) && target != null)
        {
            Player targetPlayer = target.GetComponent<Player>();
            if (targetPlayer != null && targetPlayer.IsAlive) end = targetPlayer.LockAimPoint;
            else if (target.TryGetComponent(out MagicProjectile summon) && summon.IsLiveSummon) end = summon.transform.position;
        }
        else if (current == MagicType.Razier)
        {
            // Preserve authoritative hit distance but anchor direction to the interpolated render pose.
            float distance = Vector3.Distance(owner.MagicCastPosition, end);
            end = owner.MagicCastPosition + owner.transform.forward * distance;
        }
        channelVisual.SetEndpoints(owner.MagicCastPosition, end);
    }

    public static void ShowImpact(Vector3 position, MagicType magic)
    {
        MagicStatEntry stats = Stats(magic);
        GameObject view = InstantiateVfx(stats.impactVfxPrefab, position, Quaternion.identity);
        if (view != null) Destroy(view, Mathf.Max(0.05f, stats.vfxLifetime));
        else SpawnPulse(position, MagicColor(magic), stats.radius > 0f ? stats.radius * 2f : 1.25f, 0.3f);
    }

    private static void SpawnPulse(Vector3 position, Color color, float diameter, float duration)
    {
        CombatTransientEffect.PlayPulse(position, color, diameter, duration);
    }

    public static Material CreateEffectMaterial(Color color)
    {
        Shader shader = Resources.Load<Shader>("CombatFade");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        return new Material(shader) { color = color };
    }

    private static Material CreateFadeMaterial(Material source)
    {
        Material result = CreateEffectMaterial(ReadColor(source));
        if (source == null)
            return result;
        string textureName = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        if (source.HasProperty(textureName))
        {
            result.mainTexture = source.GetTexture(textureName);
            result.mainTextureScale = source.GetTextureScale(textureName);
            result.mainTextureOffset = source.GetTextureOffset(textureName);
        }
        return result;
    }

    private static Color ReadColor(Material material)
    {
        if (material == null)
            return Color.white;
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
    }

    public static Color MagicColor(MagicType magic)
    {
        return magic switch
        {
            MagicType.Fire => new Color(1f, 0.3f, 0.08f, 0.85f),
            MagicType.Ice => new Color(0.2f, 0.75f, 1f, 0.8f),
            MagicType.Vision => new Color(0.95f, 0.3f, 1f, 0.85f),
            MagicType.Dark => new Color(0.6f, 0.08f, 0.9f, 0.9f),
            MagicType.Binding => new Color(1f, 0.65f, 0.15f, 0.8f),
            MagicType.Curse => new Color(0.85f, 0.15f, 0.65f, 0.8f),
            MagicType.Razier => new Color(0.2f, 0.8f, 1f, 0.9f),
            MagicType.Mine => new Color(1f, 0.65f, 0.15f, 0.85f),
            MagicType.Thunder => new Color(1f, 0.9f, 0.25f, 0.85f),
            MagicType.Flare => new Color(1f, 0.65f, 0.18f, 1f),
            MagicType.Smoke => new Color(0.52f, 0.56f, 0.64f, 0.2f),
            _ => new Color(0.4f, 1f, 0.8f, 0.6f)
        };
    }

    private void OnDestroy()
    {
        ClearLockCompleteVfx();
        ClearStatusEffectVfx();
        if (channelVisual != null) Destroy(channelVisual.gameObject);
        foreach (Visual visual in visuals)
        {
            if (visual.fading == null)
                continue;
            foreach (Material material in visual.fading)
                if (material != null)
                    Destroy(material);
        }
    }
}
