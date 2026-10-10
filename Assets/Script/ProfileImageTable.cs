using System;
using UnityEngine;

// Shared lookup only: BACKND/Fusion continue to store playerprofile as an integer.
[CreateAssetMenu(fileName = "ProfileImageTable", menuName = "WitchFlight/UI/Profile Image Table")]
public sealed class ProfileImageTable : ScriptableObject
{
    [Tooltip("배열 인덱스 = DataConfig/PlayerData.playerprofile. 기존 순서를 바꾸거나 중간 항목을 삭제하지 말고 뒤에 추가하세요. 사용하지 않는 번호는 None으로 남깁니다.")]
    [SerializeField] private Sprite[] profiles = Array.Empty<Sprite>();
    [Tooltip("번호가 없거나 해당 Sprite가 비어 있을 때 모든 프로필 UI가 공통으로 표시합니다. 이것도 비워두면 프로필 이미지를 숨깁니다.")]
    [SerializeField] private Sprite fallbackSprite;

    private static ProfileImageTable defaultTable;
    public static ProfileImageTable Default
    {
        get
        {
            if (defaultTable == null) defaultTable = Resources.Load<ProfileImageTable>("ProfileImageTable");
            return defaultTable;
        }
    }

    public int Count => profiles != null ? profiles.Length : 0;

    public bool TryGetSprite(int profileIndex, out Sprite sprite)
    {
        sprite = profiles != null && profileIndex >= 0 && profileIndex < profiles.Length
            ? profiles[profileIndex] : null;
        return sprite != null;
    }

    public Sprite GetSprite(int profileIndex)
    {
        return TryGetSprite(profileIndex, out Sprite sprite) ? sprite : fallbackSprite;
    }
}
