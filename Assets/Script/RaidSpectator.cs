using TMPro;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public sealed class RaidSpectator : MonoBehaviour
{
    [SerializeField] private GiantMeowRaidDirector director;
    [SerializeField] private CameraFollow follow;
    [SerializeField] private TMP_Text spectatorLabel;
    private readonly Player[] candidates = new Player[4];
    private Player observed;
    private int count;
    private float nextRefresh;

    private void LateUpdate()
    {
        Player local = Player.LocalPlayer;
        bool spectating = local != null && local.Object != null && local.Object.IsValid && !local.IsAlive && !director.Finished;
        spectatorLabel.gameObject.SetActive(spectating);
        if (!spectating) return;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + .2f;
            count = 0;
            foreach (var player in Player.ActiveCombatants)
            {
                if (player == local || player == null || player.Object == null || !player.Object.IsValid ||
                    player.Runner != local.Runner || !player.IsAlive || player.TeamIndex != local.TeamIndex || count >= candidates.Length) continue;
                int slot = count++;
                while (slot > 0 && candidates[slot - 1].ParticipantRef.RawEncoded > player.ParticipantRef.RawEncoded)
                { candidates[slot] = candidates[slot - 1]; slot--; }
                candidates[slot] = player;
            }
            for (int i = count; i < candidates.Length; i++) candidates[i] = null;
        }
        int selected = -1;
        for (int i = 0; i < count; i++) if (candidates[i] == observed) selected = i;
        int step = Input.GetKeyDown(KeyCode.UpArrow) ? 1 : Input.GetKeyDown(KeyCode.DownArrow) ? -1 : 0;
        if (count == 0) return;
        if (selected < 0 || (step != 0 && !CombatPresentation.MenuOpen))
        {
            selected = selected < 0 ? 0 : (selected + step + count) % count;
            observed = candidates[selected];
            follow.SetTarget(observed.gameObject);
            var data = PlayerData.FindPerkData(local.Runner, observed.ParticipantRef);
            spectatorLabel.richText = false;
            spectatorLabel.text = "관전 · " + (data != null ? data.playerName.ToString() : "아군") + "  [↑ / ↓ 전환]";
        }
        if (observed != null && observed.Object != null && observed.Object.IsValid && observed.IsAlive)
            follow.FollowRenderedPlayer(observed);
    }
}
