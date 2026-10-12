using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// AccountSetUI only: the saved profile ID is shared with lobby, VS and battle UI.
[DisallowMultipleComponent]
public sealed class AccountProfileUI : MonoBehaviour
{
    [Serializable]
    public sealed class ProfileChoice
    {
        [Tooltip("ProfileImageTable의 번호입니다. 버튼의 배치 순서와는 관계없습니다.")]
        [Min(0)] public int profileIndex;
        [Tooltip("변경할 프로필 이미지 버튼입니다. 기존 팝업 열기 OnClick은 그대로 유지됩니다.")]
        public Button button;
        [Tooltip("버튼 안의 ProfileImage를 연결하세요. 배경/테두리 Image가 아닙니다.")]
        public Image icon;
    }

    [Header("Current profile")]
    [Tooltip("AccountSetUI 바로 아래의 큰 ProfileImage입니다.")]
    [SerializeField] private Image currentProfileImage;
    [Tooltip("비우면 Resources/ProfileImageTable을 사용합니다. 로비/VS와 같은 테이블을 사용하세요.")]
    [SerializeField] private ProfileImageTable profileTable;
    [Header("Profile choices (selection only)")]
    [Tooltip("이미지 버튼은 변경할 번호만 선택합니다. 확정 전에는 현재 프로필이나 저장 데이터가 바뀌지 않습니다.")]
    [SerializeField] private ProfileChoice[] profileChoices = Array.Empty<ProfileChoice>();
    [Header("Shared confirmation")]
    [Tooltip("프로필/칭호 공용 확정 버튼입니다. 프로필을 선택한 경우에만 프로필 변경을 실행하며, 버튼의 활성/비활성은 변경하지 않습니다.")]
    [SerializeField] private Button confirmButton;
    [Header("Shared popup selection context")]
    [Tooltip("프로필 목록을 여는 버튼입니다. 목록을 다시 열면 이전 미확정 선택을 취소합니다.")]
    [SerializeField] private Button profileCategoryButton;
    [Tooltip("칭호 목록을 여는 버튼입니다. 칭호로 전환하면 미확정 프로필 선택을 취소합니다.")]
    [SerializeField] private Button titleCategoryButton;
    [Tooltip("칭호 목록의 루트입니다. 하위 칭호 버튼 클릭 시 프로필 선택만 취소하고, 기존 칭호 OnClick은 유지합니다.")]
    [SerializeField] private Transform titleChoicesRoot;
    [Header("Optional events")]
    [Tooltip("로컬 프로필 변경 시 호출됩니다. 서버 저장 완료 이벤트는 아닙니다.")]
    [SerializeField] private UnityEvent<int> onProfileChanged = new UnityEvent<int>();
    [SerializeField] private UnityEvent<string> onSelectionFailed = new UnityEvent<string>();

    private readonly List<(Button button, UnityAction action)> listeners = new List<(Button, UnityAction)>();
    private bool lastReady;
    private int lastProfileIndex = -1;
    private int selectedProfileIndex = -1;
    private int disabledAtFrame = -1;

    private bool ProfileReady => DatabaseManager.Instance != null &&
        DatabaseManager.Instance.IsDataConfigReady && DatabaseManager.Instance.HasLoadedProfile;

    private void OnEnable()
    {
        selectedProfileIndex = -1;
        disabledAtFrame = -1;
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        if (profileChoices != null)
        {
            foreach (ProfileChoice choice in profileChoices)
            {
                if (choice == null || choice.button == null) continue;
                int profileIndex = choice.profileIndex;
                UnityAction action = () => SelectProfile(profileIndex);
                Bind(choice.button, action);
            }
        }
        Bind(confirmButton, ConfirmSelectedProfile);
        Bind(profileCategoryButton, CancelSelection);
        Bind(titleCategoryButton, BeginTitleSelection);
        if (titleChoicesRoot != null)
            foreach (Button titleButton in titleChoicesRoot.GetComponentsInChildren<Button>(true))
                if (titleButton != confirmButton && titleButton != titleCategoryButton)
                    Bind(titleButton, BeginTitleSelection);
        DataConfig.Changed += RefreshFromDataConfig;
        RefreshFromDataConfig();
    }

    private void OnDisable()
    {
        // A persistent OnClick may close the account panel before the runtime confirm
        // listener executes. Permit that same click, but never a later hidden-panel call.
        disabledAtFrame = Time.frameCount;
        DataConfig.Changed -= RefreshFromDataConfig;
        foreach (var listener in listeners)
            if (listener.button != null) listener.button.onClick.RemoveListener(listener.action);
        listeners.Clear();
        // DatabaseManager owns auto saving, so closing this panel does not cancel a save.
    }

    private void LateUpdate()
    {
        // Login/logout snapshots can update DataConfig silently while this panel is open.
        if (lastReady != ProfileReady || lastProfileIndex != DataConfig.playerprofile)
            RefreshFromDataConfig();
    }

    private void Bind(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.AddListener(action);
        listeners.Add((button, action));
    }

    // Image buttons only choose a pending profile. Authored OnClick opens the popup.
    public void SelectProfile(int profileIndex)
    {
        CancelSelection();
        if (!isActiveAndEnabled || !ValidateSelection(profileIndex)) return;
        selectedProfileIndex = profileIndex;
    }

    // The one shared confirm button applies whichever image was selected most recently.
    public void ConfirmSelectedProfile()
    {
        if (!isActiveAndEnabled && disabledAtFrame != Time.frameCount) return;
        if (selectedProfileIndex < 0) return;
        int profileIndex = selectedProfileIndex;
        CancelSelection();
        if (!ValidateSelection(profileIndex)) return;

        bool changed = DataConfig.playerprofile != profileIndex;
        // Changed notifies the lobby UI and queues the existing account auto-save path.
        DataConfig.playerprofile = profileIndex;

        // If already connected to a room, update only our replicated PlayerData as well.
        // Otherwise PlayerData.Spawned will read the new value when we join a room.
        PlayerData local = PlayerData.Local;
        if (local != null && local.Object != null && local.Object.IsValid &&
            local.Object.HasInputAuthority && local.playerprofile != profileIndex)
            local.SetPlayerProfile(profileIndex);

        RefreshFromDataConfig();
        if (changed) onProfileChanged.Invoke(profileIndex);
    }

    // Optional cancel/back OnClick. Does not open or close any designer-owned UI.
    public void CancelSelection()
    {
        selectedProfileIndex = -1;
    }

    // Title UI owns title selection/application. This only releases our pending profile,
    // so the shared confirm button cannot also apply an old profile during a title click.
    // Dynamically created title buttons/controllers can call this entry point as well.
    public void BeginTitleSelection() => CancelSelection();

    private bool ValidateSelection(int profileIndex)
    {
        if (!ProfileReady)
        {
            Fail("로그인 후 프로필 데이터 로딩이 완료되어야 변경할 수 있습니다.");
            return false;
        }
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        if (profileTable == null || !profileTable.TryGetSprite(profileIndex, out _))
        {
            // Never save an empty placeholder or a fallback as a real profile selection.
            Fail($"ProfileImageTable에 이미지가 없는 프로필 번호입니다: {profileIndex}");
            return false;
        }
        return true;
    }

    public void RefreshFromDataConfig()
    {
        // Login/logout or a different committed profile invalidates an older draft.
        // Unrelated DataConfig edits must not reset the profile currently being selected.
        if (!ProfileReady || lastReady != ProfileReady || lastProfileIndex != DataConfig.playerprofile)
            selectedProfileIndex = -1;
        lastReady = ProfileReady;
        lastProfileIndex = DataConfig.playerprofile;
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        if (currentProfileImage != null)
        {
            Sprite sprite = lastReady && profileTable != null
                ? profileTable.GetSprite(lastProfileIndex) : null;
            currentProfileImage.sprite = sprite;
            currentProfileImage.enabled = sprite != null;
        }
        if (profileChoices == null) return;
        foreach (ProfileChoice choice in profileChoices)
        {
            if (choice == null) continue;
            Sprite icon = null;
            bool valid = profileTable != null && profileTable.TryGetSprite(choice.profileIndex, out icon);
            if (choice.button != null) choice.button.interactable = lastReady && valid;
            if (valid && choice.icon != null) choice.icon.sprite = icon;
        }
    }

    private void Fail(string message)
    {
        Debug.LogWarning(message, this);
        onSelectionFailed.Invoke(message);
    }
}
