using Fusion;

public partial class PlayerData : IInputAuthorityGained
{
    [Networked] public NetworkString<_32> DeathMatchIdentity { get; set; }
    public void InputAuthorityGained()
    {
        if (Object != null && Object.HasInputAuthority) Local = this;
    }
    public void RestoreDeathMatchOwner(bool owner)
    {
        if (Object != null && Object.HasStateAuthority) IsRoomOwner = owner;
    }
    // Monotonic host-assigned join order. PlayerRef/team slots may be reused after a leave.
    [Networked] public int DeathMatchJoinOrder { get; set; }
}
