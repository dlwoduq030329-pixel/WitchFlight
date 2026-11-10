using UnityEngine;

// Kept on the active login flow, so a closed attendance panel can open after login.
public sealed class AttendanceLoginPrompt : MonoBehaviour
{
    [SerializeField] private GameObject attendancePanel;

    private void OnEnable()
    {
        DatabaseManager.AttendanceChanged += ShowIfUnclaimed;
        DatabaseManager.DataConfigReadyChanged += ShowIfUnclaimed;
        DataConfig.Changed += ShowIfUnclaimed;
        ShowIfUnclaimed();
    }

    private void OnDisable()
    {
        DatabaseManager.AttendanceChanged -= ShowIfUnclaimed;
        DatabaseManager.DataConfigReadyChanged -= ShowIfUnclaimed;
        DataConfig.Changed -= ShowIfUnclaimed;
    }

    private void ShowIfUnclaimed()
    {
        var db = DatabaseManager.Instance;
        // Completion is set after the tutorial overlay is closed. Keep the pending
        // prompt unconsumed until that change event, even if attendance loads first.
        if (DataConfig.catTutorialCompleted != 1) return;
        if (attendancePanel == null || db == null || !db.ConsumeAttendancePrompt()) return;
        attendancePanel.transform.SetAsLastSibling();
        attendancePanel.SetActive(true);
    }
}
