using Fusion;
using UnityEngine;

// Scene-placeable test actor. Player owns the replicated health and host simulation.
// Does not register with SetPlayerObject or count as a connected match participant.
[DisallowMultipleComponent, RequireComponent(typeof(Player))]
public sealed class MagicTestBot : MonoBehaviour
{
    [SerializeField] private bool immortal = true;
    [SerializeField, Min(1f)] private float maximumHealth = 200f;
    [SerializeField, Min(1)] private int team = 99;
    [SerializeField] private bool attacks;
    [Tooltip("Fire / Ice / Binding 중에서 선택합니다.")]
    [SerializeField] private MagicType attackMagic = MagicType.Fire;
    [SerializeField, Min(1f)] private float attackDistance = 60f;
    [SerializeField, Min(0.1f)] private float attackInterval = 2f;
    [Header("Bot magic damage")]
    [Tooltip("켜면 이 봇의 화염/얼음 직접 피해량을 아래 값으로 대체합니다. 끄면 MagicStatTable 값을 사용합니다. 속박은 기존처럼 피해 없이 속박만 적용합니다.")]
    [SerializeField] private bool overrideMagicDamage;
    [SerializeField, Min(0f)] private float magicDamage = 10f;
    [Tooltip("테이블 피해량 또는 직접 지정한 피해량에 곱하는 봇 전용 배율입니다. 0이면 피해 없음, 0.5면 절반, 2면 두 배.")]
    [SerializeField, Min(0f)] private float magicDamageMultiplier = 1f;
    [Tooltip("마지막 피해 후 이 시간이 지나면 체력을 복구. 0이면 복구하지 않음.")]
    [SerializeField, Min(0f)] private float resetHealthAfterSeconds = 5f;
    private TickTimer shotTimer;
    private TickTimer healthResetTimer;
    private int seenHitSequence;
    private Player aimedPlayer;

    public bool Immortal => immortal;
    public float MaximumHealth => maximumHealth;
    public float ResolveMagicDamage(float tableDamage) => Mathf.Max(0f,
        overrideMagicDamage ? magicDamage : tableDamage) * Mathf.Max(0f, magicDamageMultiplier);
    public int Team => Mathf.Max(1, team);
    public MagicType AttackMagic => attackMagic == MagicType.Ice || attackMagic == MagicType.Binding
        ? attackMagic : MagicType.Fire;

    public void Simulate(Player self)
    {
        if (!self.Object.HasStateAuthority || !self.IsAlive) return;
        if (seenHitSequence != self.HitSequence)
        {
            seenHitSequence = self.HitSequence;
            healthResetTimer = TickTimer.CreateFromSeconds(self.Runner, Mathf.Max(0.1f, resetHealthAfterSeconds));
        }
        if (resetHealthAfterSeconds > 0f && healthResetTimer.Expired(self.Runner))
        {
            self.RestoreHealth(self.MaxHp);
            healthResetTimer = TickTimer.None;
        }
        if (!attacks || !isActiveAndEnabled) { ResetAim(self); return; }
        Player nearest = null;
        float distance = attackDistance * attackDistance;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (candidate == null || candidate.IsTestBot || candidate.Runner != self.Runner ||
                !candidate.IsTargetableBy(self)) continue;
            float sqr = (candidate.LockAimPoint - self.LockAimPoint).sqrMagnitude;
            if (sqr > distance || !MagicProjectile.HasBlastSight(self.LockAimPoint, candidate)) continue;
            distance = sqr;
            nearest = candidate;
        }
        if (nearest == null) { ResetAim(self); return; }
        // Wait a full interval on first approach; never shoot an initial surprise burst.
        if (aimedPlayer != nearest) { ResetAim(self); aimedPlayer = nearest; }
        float interval = Mathf.Max(0.1f, attackInterval);
        if (!shotTimer.IsRunning)
            shotTimer = TickTimer.CreateFromSeconds(self.Runner, interval);
        float progress = 1f - (shotTimer.RemainingTime(self.Runner) ?? 0f) / interval;
        if (!self.UpdateTestBotAim(AttackMagic, nearest, progress)) { ResetAim(self); return; }
        if (!shotTimer.Expired(self.Runner)) return;
        if (self.FireTestBotMagic(AttackMagic, nearest))
        {
            self.UpdateTestBotAim(AttackMagic, null, 0f);
            shotTimer = TickTimer.CreateFromSeconds(self.Runner, interval);
        }
    }

    private void ResetAim(Player self)
    {
        shotTimer = TickTimer.None;
        aimedPlayer = null;
        self.UpdateTestBotAim(AttackMagic, null, 0f);
    }
}
