using Fusion;

public partial class PlayerData
{
    // Monotonic host-assigned join order. PlayerRef/team slots may be reused after a leave.
    [Networked] public int DeathMatchJoinOrder { get; set; }
}
