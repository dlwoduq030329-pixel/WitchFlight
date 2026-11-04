//using System.Collections;
//using System.Collections.Generic;
using UnityEngine;

public class EndDialog : MonoBehaviour
{
    public static EndDialog Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject endDialogPanel;     // 종료 확인
    [SerializeField] private GameObject idleCheckPanel;     // 플레이 여부 확인

    [Header("Idle Check")]
    [SerializeField] private float idleTimeLimit = 420f; // 7분

    private float lastInputTime;
    private bool isIdlePanelShown = false;

    void Awake()
    {
       Instance = this;

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            //return;
        }
    }

    void Start()
    {
        lastInputTime = Time.time;
    }

    void Update()
    {
        CheckUserInput();
        CheckIdleTime();
    }
     /// <summary>
    /// 유저 입력 감지
    /// </summary>
    private void CheckUserInput()
    {
        if (Input.anyKeyDown ||
            Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2))
        {
            lastInputTime = Time.time;

            // 안내창이 떠 있었다면 다시 플레이 중으로 판단
            if (isIdlePanelShown)
            {
                CloseIdleCheckPanel();
            }
        }
    }
    /// <summary>
    /// 7분 무입력 시 "플레이 중인가요?" 안내창 표시
    /// </summary>
    private void CheckIdleTime()
    {
        if (isIdlePanelShown)
            return;

        if (Time.time - lastInputTime >= idleTimeLimit)
        {
            ShowIdleCheckPanel();
        }
    }

    // ===== Idle 안내창 =====

    private void ShowIdleCheckPanel()
    {
        if (idleCheckPanel != null)
        {
            idleCheckPanel.SetActive(true);
            isIdlePanelShown = true;
        }
    }

    public void CloseIdleCheckPanel()
    {
        if (idleCheckPanel != null)
            idleCheckPanel.SetActive(false);

        lastInputTime = Time.time;
        isIdlePanelShown = false;
    }
    public void ShowEndDialog()
    {
        if (endDialogPanel != null)
            endDialogPanel.SetActive(true);
    }

    public void ClosePanel(GameObject panelToClose)
    { 
        if (panelToClose != null) panelToClose.SetActive(false); 
    }

    public void ClosePanel()
    {
        if (endDialogPanel != null)
            endDialogPanel.SetActive(false);
    }


    public void ConfirmQuit()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

}
