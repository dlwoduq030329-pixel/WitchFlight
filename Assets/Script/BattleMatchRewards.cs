using System;
using System.Collections.Generic;
using UnityEngine;

// Completed-match rewards only. Uses the account's existing money auto-save queue.
public static class BattleMatchRewards
{
    private readonly struct PendingReward
    {
        public readonly string account, match;
        public readonly int amount;
        public PendingReward(string account, string match, int amount)
        { this.account = account; this.match = match; this.amount = amount; }
    }
    private static readonly HashSet<(string account, string match)> paid = new();
    private static readonly List<PendingReward> pending = new();
    private static bool listening;

    public static int Calculate(float damage, int kills)
    {
        // Floor once after dividing the match's total damage; never floor each hit.
        double damageReward = float.IsNaN(damage) || float.IsInfinity(damage) ? 0d : Math.Floor(Math.Max(0d, damage) / 10d);
        return (int)Math.Min(int.MaxValue, damageReward + Math.Max(0L, (long)kills) * 100L);
    }

    public static void Request(string match, float damage, int kills)
    {
        var database = DatabaseManager.Instance;
        if (database == null || !database.HasLoadedProfile || string.IsNullOrEmpty(match)) return;
        string account = database.LoadedAccountId;
        if (paid.Contains((account, match))) return;
        foreach (var reward in pending)
            if (reward.account == account && reward.match == match) return;
        pending.Add(new PendingReward(account, match, Calculate(damage, kills)));
        if (!listening)
        {
            listening = true;
            DatabaseManager.DataConfigReadyChanged += Flush;
        }
        Flush();
    }

    private static void Flush()
    {
        var database = DatabaseManager.Instance;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var reward = pending[i];
            // A result must never be credited to a different logged-in account.
            if (database == null || !database.HasLoadedProfile || database.LoadedAccountId != reward.account)
            { pending.RemoveAt(i); continue; }
            if (!database.CanModifyAccountData) continue;
            if (reward.amount > 0 && !database.TryAddMoney(reward.amount))
            {
                Debug.LogWarning("경기 보상 지급 보류: 재화 상태 또는 최대 보유량을 확인해주세요.");
                continue;
            }
            paid.Add((reward.account, reward.match));
            pending.RemoveAt(i);
            Debug.Log($"경기 완료 보상: {reward.amount:N0}원");
        }
        if (pending.Count == 0 && listening)
        {
            DatabaseManager.DataConfigReadyChanged -= Flush;
            listening = false;
        }
    }
}
