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
    [Tooltip("마지막 피해 후 이 시간이 지나면 체력을 복구. 0이면 복구하지 않음.")]
    [SerializeField, Min(0f)] private float resetHealthAfterSeconds = 5f;
    private TickTimer shotTimer;
    private TickTimer healthResetTimer;
    private int seenHitSequence;

    public bool Immortal => immortal;
    public float MaximumHealth => maximumHealth;
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
        if (!attacks) return;
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
        if (nearest == null) { shotTimer = TickTimer.None; return; }
        // Wait a full interval on first approach; never shoot an initial surprise burst.
        if (!shotTimer.IsRunning)
            shotTimer = TickTimer.CreateFromSeconds(self.Runner, Mathf.Max(0.1f, attackInterval));
        if (!shotTimer.Expired(self.Runner)) return;
        if (self.FireTestBotMagic(AttackMagic, nearest))
            shotTimer = TickTimer.CreateFromSeconds(self.Runner, Mathf.Max(0.1f, attackInterval));
    }
}
