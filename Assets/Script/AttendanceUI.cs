using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AttendanceUI : MonoBehaviour
{
    [Serializable] private sealed class DayView
    {
        public TMP_Text rewardText;
        public Image icon;
        public GameObject check;
    }
    [SerializeField] private AttendanceRewardTable rewards;
    [SerializeField] private DayView[] days = new DayView[7];
    [SerializeField] private Button checkButton;
    [SerializeField] private TMP_Text buttonText, successText;
    [SerializeField] private GameObject successPopup;
    [SerializeField] private GameObject checkImage;
    [SerializeField] private DOTweenAnimation successEntrance;
    private DOTweenAnimation[] successAnimations = Array.Empty<DOTweenAnimation>();

    private void Awake()
    {
        if (rewards == null) rewards = AttendanceRewardTable.Default;
        checkButton.onClick.AddListener(Claim);
        if (successPopup != null)
            successAnimations = successPopup.GetComponentsInChildren<DOTweenAnimation>(true);
    }
    private void OnEnable()
    {
        DatabaseManager.AttendanceChanged += Refresh;
        DatabaseManager.DataConfigReadyChanged += Refresh;
        successPopup.SetActive(false);
        Check();
    }
    private void OnDisable()
    {
        DatabaseManager.AttendanceChanged -= Refresh;
        DatabaseManager.DataConfigReadyChanged -= Refresh;
        foreach (var animation in successAnimations)
            if (animation != null && animation.tween != null && animation.tween.IsActive())
                animation.tween.Pause();
    }
    private void Check()
    {
        DatabaseManager.Instance?.RequestAttendanceCheck();
        Refresh();
    }
    private void Claim()
    {
        var db = DatabaseManager.Instance;
        if (db == null) return;
        if (db.AttendanceStatusKnown) db.ClaimAttendanceReward();
        else db.RequestAttendanceCheck();
        Refresh();
    }
    private void OnApplicationFocus(bool focused) { if (focused && isActiveAndEnabled) Check(); }
    private void Refresh()
    {
        var db = DatabaseManager.Instance;
        bool ready = db != null && db.HasLoadedProfile && db.IsDataConfigReady;
        int complete = ready ? db.AttendanceCompletedDays : 0;
        for (int i = 0; i < days.Length; i++)
        {
            var view = days[i]; if (view == null) continue;
            if (rewards != null && rewards.TryGet(i + 1, out var reward))
            {
                if (view.rewardText != null) view.rewardText.text = $"별빛재화x{reward.money}";
                if (view.icon != null && reward.icon != null && view.icon.sprite != reward.icon) view.icon.sprite = reward.icon;
            }
            if (view.check != null) view.check.SetActive(i < complete);
        }
        checkButton.interactable = ready && !db.IsAttendanceInProgress && !db.AttendanceClaimedToday;
        if (checkImage != null) checkImage.SetActive(ready && db.AttendanceClaimedToday);
        buttonText.text = !ready ? "로그인 후 확인" : db.IsAttendanceInProgress ? "출석 확인 중…" :
            db.AttendanceClaimedToday ? $"{db.AttendanceDay}일차 출석 완료" : db.AttendanceStatusKnown ? "오늘자 출석하기" : "출석 다시 확인";
        if (ready && db.ConsumeAttendanceNotice(out int day, out int amount))
        {
            if (successText != null) successText.text = $"{day}일차 출석 완료\n+{amount}";
            successPopup.SetActive(false);
            successPopup.SetActive(true);
            // Prepare every stage before starting the authored OnPlay/OnComplete chain.
            // SetActive alone cannot play animations with AutoPlay disabled.
            foreach (var animation in successAnimations)
            {
                if (animation == null || !animation.isActiveAndEnabled || !animation.isActive || !animation.isValid) continue;
                animation.CreateTween(false, false);
                var tween = animation.tween;
                if (tween == null || !tween.IsActive()) continue;
                tween.SetAutoKill(false).SetUpdate(true);
                tween.ForceInit();
                tween.Rewind();
            }
            // Only restart the entrance: its events start Circle, then FadeChul.
            if (successEntrance != null && successEntrance.tween != null && successEntrance.tween.IsActive())
                successEntrance.tween.Restart();
        }
    }
}
