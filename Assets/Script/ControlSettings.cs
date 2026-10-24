using System;
using UnityEngine;

public enum ControlAction
{
    MagicSlot1, MagicSlot2, Parry, Boost, Accelerate, TurnLeft, TurnRight, LockOn, MagicUse
}

// Local preferences only. Fusion receives the resulting actions, never another client's key map.
public static class ControlSettings
{
    private const string Prefix = "WitchFlight.Controls.v1.";
    private static readonly KeyCode[] defaults = {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Mouse1, KeyCode.LeftShift,
        KeyCode.W, KeyCode.A, KeyCode.D, KeyCode.Mouse0, KeyCode.Mouse0
    };
    private static KeyCode[] keys;
    private static float sensitivity;
    public static event Action Changed;
    public static float Sensitivity { get { EnsureLoaded(); return sensitivity; } }
    public static float SensitivityMultiplier => Sensitivity / 50f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() { keys = null; Changed = null; }

    private static void EnsureLoaded()
    {
        if (keys != null) return;
        keys = (KeyCode[])defaults.Clone();
        float saved = PlayerPrefs.GetFloat(Prefix + "Sensitivity", 50f);
        sensitivity = float.IsNaN(saved) || float.IsInfinity(saved) ? 50f : Mathf.Clamp(saved, 1f, 100f);
        for (int i = 0; i < keys.Length; i++)
        {
            KeyCode key = (KeyCode)PlayerPrefs.GetInt(Prefix + (ControlAction)i, (int)defaults[i]);
            if (IsAssignable(key)) keys[i] = Normalize(key);
        }
        keys[(int)ControlAction.MagicUse] = keys[(int)ControlAction.LockOn];
        // Corrupted/manual preference edits must not disable unrelated controls.
        if (!ValidBindings(keys)) keys = (KeyCode[])defaults.Clone();
    }

    public static KeyCode GetKey(ControlAction action) { EnsureLoaded(); return keys[(int)action]; }
    public static bool Held(ControlAction action) => Read(action, false);
    public static bool Down(ControlAction action) => Read(action, true);
    private static bool Read(ControlAction action, bool down)
    {
        KeyCode key = GetKey(action);
        bool pressed = down ? Input.GetKeyDown(key) : Input.GetKey(key);
        KeyCode other = key == KeyCode.LeftShift ? KeyCode.RightShift :
            key == KeyCode.LeftControl ? KeyCode.RightControl :
            key == KeyCode.LeftAlt ? KeyCode.RightAlt : KeyCode.None;
        return pressed || (other != KeyCode.None && (down ? Input.GetKeyDown(other) : Input.GetKey(other)));
    }

    public static void SetSensitivity(float value)
    {
        EnsureLoaded();
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        sensitivity = Mathf.Clamp(Mathf.Round(value), 1f, 100f);
        PlayerPrefs.SetFloat(Prefix + "Sensitivity", sensitivity);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static bool TryRebind(ControlAction action, KeyCode requested, out string error)
    {
        EnsureLoaded();
        KeyCode key = Normalize(requested);
        error = "";
        if (!IsAssignable(key)) { error = "예약된 키 · 다른 키 입력"; return false; }
        KeyCode old = keys[(int)action];
        var next = (KeyCode[])keys.Clone();
        next[(int)action] = key;
        // Lock and cast are one binding: hold to aim, release to fire stays unchanged.
        if (action == ControlAction.LockOn || action == ControlAction.MagicUse)
        {
            next[(int)ControlAction.LockOn] = key;
            next[(int)ControlAction.MagicUse] = key;
        }
        for (int i = 0; i < next.Length; i++)
            if (i != (int)action && next[i] == key && !CanShare(action, (ControlAction)i)) next[i] = old;
        if (!ValidBindings(next)) { error = "중복 키 · 다른 키 입력"; return false; }
        keys = next;
        for (int i = 0; i < keys.Length; i++) PlayerPrefs.SetInt(Prefix + (ControlAction)i, (int)keys[i]);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }

    private static bool CanShare(ControlAction a, ControlAction b) =>
        (a == ControlAction.LockOn && b == ControlAction.MagicUse) ||
        (a == ControlAction.MagicUse && b == ControlAction.LockOn);

    private static bool ValidBindings(KeyCode[] bindings)
    {
        for (int i = 0; i < bindings.Length; i++)
            for (int j = i + 1; j < bindings.Length; j++)
                if (bindings[i] == bindings[j] && !CanShare((ControlAction)i, (ControlAction)j)) return false;
        return true;
    }

    public static bool IsAssignable(KeyCode key)
    {
        // These inputs still own fixed game/UI actions (deceleration, third slot, menus, perks, emotes).
        return Enum.IsDefined(typeof(KeyCode), key) && key != KeyCode.None && key < KeyCode.JoystickButton0 &&
            key != KeyCode.Escape && key != KeyCode.Tab && key != KeyCode.Space && key != KeyCode.T &&
            key != KeyCode.S && key != KeyCode.Alpha3;
    }

    private static KeyCode Normalize(KeyCode key) => key == KeyCode.RightShift ? KeyCode.LeftShift :
        key == KeyCode.RightControl ? KeyCode.LeftControl : key == KeyCode.RightAlt ? KeyCode.LeftAlt : key;

    public static string KeyLabel(KeyCode key)
    {
        key = Normalize(key);
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
        switch (key)
        {
            case KeyCode.Mouse0: return "좌클릭";
            case KeyCode.Mouse1: return "우클릭";
            case KeyCode.Mouse2: return "휠 클릭";
            case KeyCode.Mouse3: return "마우스 4";
            case KeyCode.Mouse4: return "마우스 5";
            case KeyCode.LeftShift: return "Shift";
            case KeyCode.LeftControl: return "Ctrl";
            case KeyCode.LeftAlt: return "Alt";
            default: return key.ToString();
        }
    }
}
