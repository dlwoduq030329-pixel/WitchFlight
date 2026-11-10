using UnityEngine;

// Shared decision policy for network match bots and the completely offline balance lab.
public static class BotHomingEvasion
{
    public static bool TryDashDelay(MatchBotSettings settings, out float delay)
    {
        delay = 0f;
        if (Random.value >= Mathf.Clamp01(settings.dashUseChance)) return false;
        bool late = Random.value < Mathf.Clamp01(settings.dashMistakeChance);
        float minimum = Mathf.Max(0f, settings.dashMistakeDelay.x);
        delay = late ? Random.Range(minimum, Mathf.Max(minimum, settings.dashMistakeDelay.y)) : 0f;
        return true;
    }
    public static bool CanExploit(float currentRate, float initialRate, float arrival, float weakRatio)
        => arrival >= .35f && currentRate <= Mathf.Max(0f, initialRate) * Mathf.Clamp01(weakRatio);

    public static bool TryPlan(float currentRate, float initialRate, float arrival, bool canDash,
        MatchBotSettings settings, out float angle, out float delay, out bool dash)
    {
        angle = delay = 0f; dash = false;
        if (Random.value >= Mathf.Clamp01(settings.evasionUseChance)) return false;
        bool weak = CanExploit(currentRate, initialRate, arrival, settings.evasionWeakTurnRatio);
        if (!weak && (!settings.useCombatDash || !canDash)) return false;
        bool mistake = Random.value < Mathf.Clamp01(settings.evasionMistakeChance);
        angle = mistake ? 25f : weak ? Mathf.Clamp(settings.evasionTurnAngle, 45f, 150f) : 65f;
        delay = mistake ? Mathf.Max(0f, settings.evasionMistakeDelay) : 0f;
        dash = !weak;
        return true;
    }

    public static Vector3 ChooseSide(Vector3 forward, Vector3 shotHeading, float angle, out Vector3 alternative)
    {
        Vector3 left = Quaternion.AngleAxis(-angle, Vector3.up) * forward;
        Vector3 right = Quaternion.AngleAxis(angle, Vector3.up) * forward;
        bool chooseLeft = Mathf.Abs(Vector3.Dot(left, shotHeading)) <= Mathf.Abs(Vector3.Dot(right, shotHeading));
        alternative = chooseLeft ? right : left;
        return chooseLeft ? left : right;
    }
}
