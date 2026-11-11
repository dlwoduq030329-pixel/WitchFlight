using Fusion;
using UnityEngine;

// Existing battle keeps its flag-owned smoke; deathmatch has a separate network state.
public static class CombatSmoke
{
    public static bool CanCreate(NetworkRunner runner) => DeathMatchState.Instance != null
        ? DeathMatchState.Instance.CanCreateSmoke(runner)
        : BattleFlag.Instance != null && BattleFlag.Instance.CanCreateSmoke(runner);

    public static bool Create(Player caster, MagicStatEntry stats) => DeathMatchState.Instance != null
        ? DeathMatchState.Instance.CreateSmoke(caster, stats)
        : BattleFlag.Instance != null && BattleFlag.Instance.CreateSmoke(caster, stats);

    public static bool Contains(NetworkRunner runner, Vector3 point) => DeathMatchState.Instance != null
        ? DeathMatchState.Instance.Runner == runner && DeathMatchState.Instance.ContainsSmoke(point)
        : BattleFlag.Instance != null && BattleFlag.Instance.Runner == runner && BattleFlag.Instance.ContainsSmoke(point);

    public static bool ContainsEnemy(NetworkRunner runner, Vector3 point, int team) => DeathMatchState.Instance != null
        ? DeathMatchState.Instance.Runner == runner && DeathMatchState.Instance.ContainsEnemySmoke(point, team)
        : BattleFlag.Instance != null && BattleFlag.Instance.Runner == runner && BattleFlag.Instance.ContainsEnemySmoke(point, team);
    public static float LockDurationMultiplier(NetworkRunner runner, Vector3 point, int team) => DeathMatchState.Instance != null
        ? DeathMatchState.Instance.Runner == runner ? DeathMatchState.Instance.SmokeLockDurationMultiplier(point, team) : 1f
        : BattleFlag.Instance != null && BattleFlag.Instance.Runner == runner ? BattleFlag.Instance.SmokeLockDurationMultiplier(point, team) : 1f;
}
