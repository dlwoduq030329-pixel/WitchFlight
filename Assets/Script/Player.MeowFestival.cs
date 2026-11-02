using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private TickTimer FestivalTimer { get; set; }
    [Networked] private float FestivalMultiplier { get; set; }
    [Networked] public Vector3 FestivalOrigin { get; private set; }
    [Networked] public int FestivalSequence { get; private set; }
    private float festivalHealRate, festivalBudget, festivalHealElapsed;
    private PlayerRef festivalHealer;
    private MeowFestivalVisual festivalVisual;
    public bool HasMeowFestival => networkSpawned && Object != null && Object.IsValid && IsAlive && TimerIsActive(FestivalTimer);
    private float FestivalRecoveryBonus => HasMeowFestival ? Mathf.Max(1f, FestivalMultiplier) : 1f;

    private void ApplyMeowFestival(Player caster, MagicStatEntry entry)
    {
        if (!Object.HasStateAuthority || !IsAlive) return;
        if (!HasMeowFestival) festivalHealElapsed = 0f;
        festivalBudget = Mathf.Max(0f, entry.effectDuration);
        FestivalTimer = TickTimer.CreateFromSeconds(Runner, festivalBudget);
        FestivalMultiplier = Mathf.Max(1f, entry.festivalRecoveryMultiplier);
        festivalHealRate = Mathf.Max(0f, entry.festivalHealPerSecond);
        festivalHealer = caster.ParticipantRef;
        FestivalOrigin = caster.MagicCastPosition;
        FestivalSequence++;
    }
    private void TickMeowFestival()
    {
        if (!Object.HasStateAuthority || !IsAlive || festivalBudget <= 0f) return;
        float seconds = Mathf.Min(Runner.DeltaTime, festivalBudget);
        festivalBudget = Mathf.Max(0f, festivalBudget - seconds);
        festivalHealElapsed += seconds;
        if (festivalHealElapsed >= 1f || festivalBudget <= .0001f)
        {
            float healingSeconds = festivalBudget <= .0001f ? festivalHealElapsed : Mathf.Floor(festivalHealElapsed);
            RestoreHealth(festivalHealRate * healingSeconds, festivalHealer, false);
            festivalHealElapsed = Mathf.Max(0f, festivalHealElapsed - healingSeconds);
        }
    }
    private void ResetMeowFestival()
    {
        FestivalTimer = TickTimer.None;
        festivalBudget = festivalHealElapsed = 0f;
    }
    private void RenderMeowFestival()
    {
        bool active = HasMeowFestival && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        if (active && festivalVisual == null)
        {
            var root = new GameObject("Meow Festival visuals");
            root.transform.SetParent(transform, false);
            festivalVisual = root.AddComponent<MeowFestivalVisual>();
            festivalVisual.Initialize(this);
        }
        if (festivalVisual != null) festivalVisual.Show(active);
    }
}
