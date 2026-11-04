using Fusion;
using UnityEngine;

public sealed class RaidCoreTarget : MagicProjectile
{
    [Networked] private NetworkId BossId { get; set; }
    [Networked] private int Slot { get; set; }
    private GiantMeowBoss boss;
    private Renderer coreRenderer;
    private MaterialPropertyBlock properties;
    private float nextResolve;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private void Awake()
    {
        coreRenderer = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();
    }
    public void Configure(GiantMeowBoss owner, int index)
    {
        boss = owner;
        BossId = owner.Object.Id;
        Slot = index;
        ConstructKind = MagicConstructKind.Summon;
        Magic = MagicType.Vision;
        Shooter = PlayerRef.None;
        ShooterTeam = 99;
        ShooterId = owner.Object.Id;
        Finished = false;
    }
    private void ResolveBoss()
    {
        if (boss != null || Time.unscaledTime < nextResolve) return;
        nextResolve = Time.unscaledTime + .5f;
        if (Runner.TryFindObject(BossId, out var obj)) boss = obj.GetComponent<GiantMeowBoss>();
    }
    public override bool IsLiveSummon => base.IsLiveSummon && boss != null && boss.Object != null && boss.Object.IsValid &&
        boss.Absorbing && boss.CoreHealth[Slot] > 0f;
    public override float SummonMaxHealth => boss != null ? boss.Settings.coreHealth : 1f;
    public override float SummonHealthFraction => IsLiveSummon ? boss.CoreHealth[Slot] / Mathf.Max(1f, SummonMaxHealth) : 0f;
    public override float SummonHitRadius => boss != null ? boss.Settings.coreRadius : 0f;
    public override bool ReceiveSummonHit(MagicStatEntry attack, int team, NetworkId attacker) =>
        IsLiveSummon && Object.HasStateAuthority && team != ShooterTeam && boss.DamageCore(Slot, attack, attacker);
    public override void FixedUpdateNetwork()
    {
        ResolveBoss();
        if (boss != null && boss.Object != null && boss.Object.IsValid) transform.position = boss.CorePositions[Slot];
    }
    public override void Render()
    {
        ResolveBoss();
        bool visible = IsLiveSummon;
        coreRenderer.enabled = visible;
        if (!visible) return;
        transform.position = boss.CorePositions[Slot];
        transform.localScale = Vector3.one * SummonHitRadius * 2f;
        properties.SetColor(ColorId, Color.Lerp(new Color(1f, .75f, .1f, 1f), new Color(.1f, 1f, 1f, 1f), SummonHealthFraction));
        coreRenderer.SetPropertyBlock(properties);
    }
}
