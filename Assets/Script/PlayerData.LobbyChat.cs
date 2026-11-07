using System;
using System.Text;
using Fusion;

public partial class PlayerData
{
    public const int LobbyChatCharacterLimit = 100;
    private double nextLobbyChatAt = -1;

    public bool SendLobbyChat(string message)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority ||
            NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsWaitingInCodeRoom) return false;
        message = CleanLobbyChat(message);
        if (message.Length == 0) return false;
        RPC_RequestLobbyChat(message);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestLobbyChat(string message)
    {
        if (!IsLoadoutInitialized || IsMatchBot || NetworkGameManager.Instance == null ||
            !NetworkGameManager.Instance.IsWaitingInCodeRoom || Runner.SimulationTime < nextLobbyChatAt) return;
        message = CleanLobbyChat(message);
        if (message.Length == 0) return;
        nextLobbyChatAt = Runner.SimulationTime + .35;
        // Identity comes from this input-authority owner's replicated account, not message input.
        RPC_ReceiveLobbyChat(playerName.ToString(), message, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReceiveLobbyChat(string sender, string message, long unixTime)
    {
        MainRoomChatUI.Receive(Runner, sender, message, unixTime);
    }

    internal static string CleanLobbyChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        var clean = new StringBuilder(Math.Min(message.Length, LobbyChatCharacterLimit));
        for (int i = 0; i < message.Length && clean.Length < LobbyChatCharacterLimit; i++)
        {
            char c = message[i];
            if (char.IsControl(c)) { if (c == '\n' || c == '\r' || c == '\t') clean.Append(' '); continue; }
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < message.Length && char.IsLowSurrogate(message[i + 1]) && clean.Length + 2 <= LobbyChatCharacterLimit)
                { clean.Append(c); clean.Append(message[++i]); }
                continue;
            }
            if (!char.IsLowSurrogate(c)) clean.Append(c);
        }
        return clean.ToString().Trim();
    }
}
