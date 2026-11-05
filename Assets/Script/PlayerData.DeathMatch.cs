using Fusion;

public partial class PlayerData : IInputAuthorityGained
{
    public static event System.Action<bool> DeathMatchScoreChanged;
    private void NotifyDeathMatchRankingChanged() => NotifyDeathMatchScore(true);
    private void NotifyDeathMatchStatsChanged() => NotifyDeathMatchScore(false);
    private void NotifyDeathMatchScore(bool ranking)
    {
        if (NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsDeathMatch)
            DeathMatchScoreChanged?.Invoke(ranking);
    }
    [Networked] public NetworkString<_32> DeathMatchIdentity { get; set; }
    public void InputAuthorityGained()
    {
        if (Object != null && Object.HasInputAuthority) Local = this;
        NotifyDeathMatchRankingChanged();
    }
    public void RestoreDeathMatchOwner(bool owner)
    {
        if (Object != null && Object.HasStateAuthority) IsRoomOwner = owner;
    }
    // Monotonic host-assigned join order. PlayerRef/team slots may be reused after a leave.
    [Networked, OnChangedRender(nameof(NotifyDeathMatchRankingChanged))] public int DeathMatchJoinOrder { get; set; }
}
