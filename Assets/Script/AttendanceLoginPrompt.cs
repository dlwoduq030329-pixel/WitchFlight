using UnityEngine;

// Kept on the active login flow, so a closed attendance panel can open after login.
public sealed class AttendanceLoginPrompt : MonoBehaviour
{
    [SerializeField] private GameObject attendancePanel;

    private void OnEnable()
    {
        DatabaseManager.AttendanceChanged += ShowIfUnclaimed;
        DatabaseManager.DataConfigReadyChanged += ShowIfUnclaimed;
        ShowIfUnclaimed();
    }

    private void OnDisable()
    {
        DatabaseManager.AttendanceChanged -= ShowIfUnclaimed;
        DatabaseManager.DataConfigReadyChanged -= ShowIfUnclaimed;
    }

    private void ShowIfUnclaimed()
    {
        var db = DatabaseManager.Instance;
        if (attendancePanel == null || db == null || !db.ConsumeAttendancePrompt()) return;
        attendancePanel.transform.SetAsLastSibling();
        attendancePanel.SetActive(true);
    }
}
