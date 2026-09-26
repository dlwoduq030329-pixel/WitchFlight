using UnityEngine;

public class PlayerEquipment : MonoBehaviour
{
    [Header("Pre-placed objects (MagicType enum index order)")]
    [SerializeField] private GameObject[] magicStaffObjects;

    [Header("Pre-placed objects (HatType enum index order)")]
    [SerializeField] private GameObject[] hatObjects;

    [Header("Pre-placed objects (BroomType enum index order)")]
    [SerializeField] private GameObject[] broomObjects;

    private GameObject magicStaff1;
    private GameObject magicStaff2;
    private MagicType magic1;
    private MagicType magic2;
    private int currentMagicSlot;

    public void Init(MagicType magic1, MagicType magic2, HatType hat, BroomType broom)
    {
        ApplyLoadout(hat, broom, magic1, magic2);
    }

    // All objects are authored as children of PlayerPrefab. This method only
    // switches their active state; it never instantiates or destroys equipment.
    public void ApplyLoadout(HatType selectedHat, BroomType selectedBroom,
        MagicType selectedMagic1, MagicType selectedMagic2)
    {
        magic1 = selectedMagic1;
        magic2 = selectedMagic2;

        SetAllInactive(magicStaffObjects);
        magicStaff1 = FindMagicObject(magic1);
        magicStaff2 = FindMagicObject(magic2);
        currentMagicSlot = 0;
        ChangeMagic(1);

        EquipHat((int)selectedHat);
        EquipBroom((int)selectedBroom);
    }

    public void ChangeMagic(int slot)
    {
        if (slot != 1 && slot != 2)
            return;

        if (currentMagicSlot == slot)
            return;

        if (magicStaff1 != null)
            magicStaff1.SetActive(false);
        if (magicStaff2 != null)
            magicStaff2.SetActive(false);

        GameObject selectedStaff = slot == 1 ? magicStaff1 : magicStaff2;
        if (selectedStaff != null)
            selectedStaff.SetActive(true);

        currentMagicSlot = slot;
    }

    public void EquipMagic(int selectedMagic1, int selectedMagic2)
    {
        magic1 = (MagicType)selectedMagic1;
        magic2 = (MagicType)selectedMagic2;
        SetAllInactive(magicStaffObjects);
        magicStaff1 = FindMagicObject(magic1);
        magicStaff2 = FindMagicObject(magic2);
        currentMagicSlot = 0;
        ChangeMagic(1);
    }

    public void EquipHat(int index)
    {
        SetOnlyActive(hatObjects, index);
    }

    public void EquipBroom(int index)
    {
        SetOnlyActive(broomObjects, index);
    }

    private GameObject FindMagicObject(MagicType magic)
    {
        int index = (int)magic;
        if (magicStaffObjects == null || index <= 0 || index >= magicStaffObjects.Length)
            return null;

        return magicStaffObjects[index];
    }

    private void SetOnlyActive(GameObject[] objects, int activeIndex)
    {
        if (objects == null)
            return;

        for (int index = 0; index < objects.Length; index++)
        {
            if (objects[index] != null)
                objects[index].SetActive(index == activeIndex && index > 0);
        }
    }

    private void SetAllInactive(GameObject[] objects)
    {
        if (objects == null)
            return;

        foreach (GameObject equipmentObject in objects)
        {
            if (equipmentObject != null)
                equipmentObject.SetActive(false);
        }
    }
}