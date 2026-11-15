using Fusion;

public partial class PlayerData
{
    [Networked] public int EmoteUpId { get; private set; } = 1;
    [Networked] public int EmoteRightId { get; private set; } = 2;
    [Networked] public int EmoteDownId { get; private set; } = 3;
    [Networked] public int EmoteLeftId { get; private set; } = 4;

    private bool sentLobbyLoadout;
    private (int hat, int broom, int first, int second, int up, int right, int down, int left) lastLobbyLoadout;

    private void StartLobbyLoadoutSync()
    {
        sentLobbyLoadout = false;
        DataConfig.Changed += SyncLobbyLoadout;
        SyncLobbyLoadout();
    }

    private void StopLobbyLoadoutSync() => DataConfig.Changed -= SyncLobbyLoadout;

    private void SyncLobbyLoadout()
    {
        var manager = NetworkGameManager.Instance;
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority ||
            manager == null || manager.Runner != Runner || !manager.IsWaitingInCodeRoom) return;
        var snapshot = (DataConfig.hatIndex, DataConfig.broomIndex, DataConfig.magic1Index,
            DataConfig.magic2Index, DataConfig.GetEmote(0), DataConfig.GetEmote(1),
            DataConfig.GetEmote(2), DataConfig.GetEmote(3));
        // Money, missions and other account edits must not resend equipment.
        if (sentLobbyLoadout && lastLobbyLoadout.Equals(snapshot)) return;
        lastLobbyLoadout = snapshot;
        sentLobbyLoadout = true;
        RPC_UpdateLobbyLoadout((HatType)snapshot.Item1, (BroomType)snapshot.Item2,
            (MagicType)snapshot.Item3, (MagicType)snapshot.Item4,
            snapshot.Item5, snapshot.Item6, snapshot.Item7, snapshot.Item8);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_UpdateLobbyLoadout(HatType selectedHat, BroomType selectedBroom,
        MagicType first, MagicType second, int up, int right, int down, int left)
    {
        var manager = NetworkGameManager.Instance;
        // Recheck on the host: a delayed UI click cannot modify battle equipment.
        if (manager == null || manager.Runner != Runner || !manager.IsWaitingInCodeRoom ||
            !IsLoadoutInitialized || IsMatchBot) return;
        hat = NormalizeHat(selectedHat);
        broom = NormalizeBroom(selectedBroom);
        magic1 = NormalizeMagic(first, MagicType.Fire);
        magic2 = NormalizeMagic(second, MagicType.Ice);
        InitialMagic1 = magic1;
        InitialMagic2 = magic2;
        EmoteUpId = NormalizeLobbyEmote(up, 1);
        EmoteRightId = NormalizeLobbyEmote(right, 2);
        EmoteDownId = NormalizeLobbyEmote(down, 3);
        EmoteLeftId = NormalizeLobbyEmote(left, 4);
    }

    private static int NormalizeLobbyEmote(int id, int fallback) =>
        EmoteTable.Default != null && EmoteTable.Default.TryGet(id, out _) ? id : fallback;
}
