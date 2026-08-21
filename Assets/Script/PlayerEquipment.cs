using UnityEngine;

public class PlayerEquipment : MonoBehaviour
{
    [Header("지팡이를 장착할 손")]
    [SerializeField]
    private Transform hand;

    [Header("지팡이를 장착할 손")]
    [SerializeField]
    private Transform broomhand;

    [Header("마법 지팡이 프리팹")]
    [SerializeField]
    private GameObject[] magicStaffPrefabs;

    private GameObject magicStaff1;
    private GameObject magicStaff2;

    private MagicType magic1;
    private MagicType magic2;

    private int currentMagicSlot = 0;


    public void Init(
        MagicType magic1,
        MagicType magic2,
        HatType hat,
        BroomType broom)
    {
        this.magic1 = magic1;
        this.magic2 = magic2;

        // 마법 지팡이 2개를 미리 생성
        magicStaff1 = CreateMagicStaff(magic1);
        magicStaff2 = CreateMagicStaff(magic2);

        // 처음에는 1번 지팡이 사용
        ChangeMagic(1);

        // 모자 / 빗자루
        EquipHat((int)hat);
        EquipBroom((int)broom);
    }


    private GameObject CreateMagicStaff(MagicType magic)
    {
        GameObject prefab = FindMagicPrefab(magic);

        if (prefab == null)
            return null;

        GameObject staff = Instantiate(
            prefab,
            hand
        );

        staff.transform.localPosition = Vector3.zero;
        staff.transform.localRotation = Quaternion.identity;

        staff.SetActive(false);

        return staff;
    }


    private GameObject FindMagicPrefab(MagicType magic)
    {
        int index = (int)magic;

        if (index < 0 || index >= magicStaffPrefabs.Length)
            return null;

        return magicStaffPrefabs[index];
    }


    public void ChangeMagic(int slot)
    {
        if (slot != 1 && slot != 2)
            return;

        // 현재 장비와 같으면 아무것도 하지 않음
        if (currentMagicSlot == slot)
            return;

        // 기존 지팡이 끄기
        if (magicStaff1 != null)
            magicStaff1.SetActive(false);

        if (magicStaff2 != null)
            magicStaff2.SetActive(false);


        // 선택한 지팡이 켜기
        if (slot == 1)
        {
            if (magicStaff1 != null)
                magicStaff1.SetActive(true);
        }
        else
        {
            if (magicStaff2 != null)
                magicStaff2.SetActive(true);
        }

        currentMagicSlot = slot;
    }


    public void EquipMagic(int magic1, int magic2)
    {
        // 필요하면 기존 인터페이스 유지
        this.magic1 = (MagicType)magic1;
        this.magic2 = (MagicType)magic2;
    }


    public void EquipHat(int index)
    {

    }


    public void EquipBroom(int index)
    {

    }
}