using UnityEngine;
using UnityEngine.UI;

public class GridHPBar : MonoBehaviour
{
    public Transform gridParent; // Grid_Parent를 드래그 앤 드롭
    private Image[] hpCells;
    private int displayedCellCount = -1;

    [Header("테스트용 현재 HP 비율 (0.0 ~ 1.0)")]
    [Range(0f, 1f)] public float currentPercent = 1f;

    void Awake()
    {
        UpdateHP(currentPercent);
    }

    // 실제 게임 로직에서 호출할 함수 (0.0 ~ 1.0 사이의 값 전달)
    public void UpdateHP(float percent)
    {
        currentPercent = Mathf.Clamp01(percent);

        // UI binding may run before Awake or while this bar is inactive.
        if (hpCells == null && gridParent != null)
            hpCells = gridParent.GetComponentsInChildren<Image>(true);

        if (hpCells == null || hpCells.Length == 0) return;

        // 예: 칸이 총 10개라면, 켜져야 할 칸의 개수를 구함
        int activeCellsCount = Mathf.RoundToInt(hpCells.Length * currentPercent);
        if (activeCellsCount == displayedCellCount) return;
        displayedCellCount = activeCellsCount;

        for (int i = 0; i < hpCells.Length; i++)
        {
            if (hpCells[i] == null) continue;
            Color color = hpCells[i].color;

            if (i < activeCellsCount)
            {
                color.a = 1f; // 채워진 칸은 불투명하게 (100%)
            }
            else
            {
                color.a = 0f; // 깎인 칸은 완전히 투명하게 (0%) 
                // * 만약 살짝 어두운 배경 잔상을 남기고 싶다면 0.15f 정도로 설정하세요!
            }

            hpCells[i].color = color;
        }
    }

    // Connect linkuserinfo's Dynamic PlayerData event to this method.
    public void UpdateFromPlayerData(PlayerData data)
    {
        bool valid = data != null && data.Object != null && data.Object.IsValid &&
                     data.Runner != null && data.Runner.IsRunning;
        UpdateHP(valid && data.BattleMaxHp > 0f
            ? data.BattleCurrentHp / data.BattleMaxHp
            : 0f);
    }

    // ⭐ [중요] 버튼 클릭 이벤트에 연결해서 테스트할 함수
    // 버튼을 누를 때마다 HP를 10%(0.1)씩 깎아줍니다.
    public void Test_DecreaseHP()
    {
        UpdateHP(currentPercent - 0.1f);
    }
}
