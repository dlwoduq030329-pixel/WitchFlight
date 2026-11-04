using Fusion;
using UnityEngine;

public sealed class RaidMinion : MagicProjectile
{
    [Networked] private NetworkId BossId { get; set; }
    [Networked] public bool Active { get; private set; }
    [Networked] private float Health { get; set; }
    [Networked] private int BodyIndex { get; set; }
    [Networked] private int AttackSequence { get; set; }
    [Networked] private TickTimer Expiry { get; set; }
    private GiantMeowBoss boss;
    private CatAvatar avatar;
    private NetworkTransform movement;
    private Player target;
    private int slot, shownBody, shownAttack;
    private float refresh, attack, nextResolve;

    public void Configure(GiantMeowBoss owner)
    {
        boss = owner; BossId = owner.Object.Id;
        ConstructKind = MagicConstructKind.Summon;
        Magic = MagicType.Vision; Shooter = PlayerRef.None; ShooterTeam = 99; ShooterId = owner.Object.Id;
    }
    public override void Spawned()
    {
        base.Spawned();
        if (boss == null && Runner.TryFindObject(BossId, out var owner)) boss = owner.GetComponent<GiantMeowBoss>();
        movement = GetComponent<NetworkTransform>();
        avatar = CatAvatar.Create(transform, 1, 1, true);
        if (avatar != null)
        {
            avatar.transform.localScale = Vector3.one * 3f;
            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true)) animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            gameObject.AddComponent<RaidMinionFeedback>().Initialize(avatar, this);
            avatar.gameObject.SetActive(false);
        }
    }
    public void Activate(Vector3 position, int body, int index)
    {
        if (!Object.HasStateAuthority) return;
        slot = index; BodyIndex = body; Health = boss.Settings.minionHealth;
        Finished = false; Active = true; refresh = 0f; attack = .6f + index * .08f;
        Expiry = TickTimer.CreateFromSeconds(Runner, boss.Settings.minionLifetime);
        movement.Teleport(position, boss.transform.rotation);
    }
    public override bool IsLiveSummon => base.IsLiveSummon && Active && boss != null && boss.Object != null && boss.Object.IsValid && boss.IsTargetable;
    public override float SummonMaxHealth => boss != null ? boss.Settings.minionHealth : 1f;
    public override float SummonHealthFraction => Health / Mathf.Max(1f, SummonMaxHealth);
    public override float SummonHitRadius => 1.5f;
    public override bool ReceiveSummonHit(MagicStatEntry hit, int team, NetworkId attacker)
    {
        if (!Object.HasStateAuthority || !IsLiveSummon || team == ShooterTeam || hit.damage <= 0 || float.IsNaN(hit.damage) || float.IsInfinity(hit.damage)) return false;
        float before = Health;
        Health = Mathf.Max(0, Health - hit.damage);
        boss.RecordDealt(attacker, before - Health, Health <= 0f);
        if (Health <= 0) { Active = false; Finished = true; }
        if (Runner.TryFindObject(attacker, out var source) && source != null) source.GetComponent<Player>()?.ConfirmSummonHit();
        return true;
    }
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !Active) return;
        if (boss == null || boss.Object == null || !boss.Object.IsValid || !boss.IsTargetable || Expiry.Expired(Runner)) { Active = false; Finished = true; return; }
        refresh -= Runner.DeltaTime;
        if (refresh <= 0) { refresh = .3f; target = boss.MinionTarget(slot); }
        if (target == null || target.Object == null || !target.Object.IsValid || !target.IsAlive) return;
        Vector3 delta = target.LockAimPoint - transform.position;
        if (delta.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(delta), 120f * Runner.DeltaTime);
        if (delta.sqrMagnitude > 400f)
        {
            Vector3 step = delta.normalized * boss.Settings.minionSpeed * Runner.DeltaTime;
            if (Runner.GetPhysicsScene().SphereCast(transform.position, SummonHitRadius, step.normalized, out var hit, step.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 slide = Vector3.ProjectOnPlane(step, hit.normal);
                if (slide.sqrMagnitude < .001f) slide = Vector3.Cross(hit.normal, Vector3.up) * step.magnitude;
                step = MagicProjectile.HasSummonSight(transform.position, transform.position + slide) ? slide : Vector3.zero;
            }
            Vector3 position = transform.position + step;
            if (boss.Settings.boundary != null) position = boss.Settings.boundary.ClampBotPosition(position);
            transform.position = position;
        }
        attack -= Runner.DeltaTime;
        if (attack <= 0f && MagicProjectile.HasSummonSight(transform.position, target.LockAimPoint))
        {
            attack = boss.Settings.minionAttackInterval;
            boss.FireMinion(transform.position, target.LockAimPoint);
            AttackSequence++;
        }
    }
    public override void Render()
    {
        if (boss == null && Time.unscaledTime >= nextResolve)
        {
            nextResolve = Time.unscaledTime + .5f;
            if (Runner.TryFindObject(BossId, out var owner) && owner != null) boss = owner.GetComponent<GiantMeowBoss>();
        }
        if (avatar == null) return;
        avatar.gameObject.SetActive(IsLiveSummon);
        if (!IsLiveSummon) return;
        if (shownBody != BodyIndex) { shownBody = BodyIndex; avatar.SetAppearance(BodyIndex, 1); }
        if (shownAttack != AttackSequence) { shownAttack = AttackSequence; avatar.Play(CatAnimation.Punch); }
    }
}
