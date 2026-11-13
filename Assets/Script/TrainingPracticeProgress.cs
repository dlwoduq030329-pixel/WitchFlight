// Observes accepted gameplay state, never raw button presses. Reset for every exercise.
public sealed class TrainingPracticeProgress
{
    public bool IncreasedSpeed { get; private set; }
    public bool SelectedSecond { get; private set; }
    public bool Charged { get; private set; }
    private int lastSpeed, castAtStart, parryAtStart, choicesAtStart;
    private float boostSeconds;
    private float recoverySeconds, lastMana, lastDash;
    private bool wasInRecoveryArea, recoveredMana, recoveredDash;

    public void Reset(int speed, int casts, int parries, int choices)
    {
        IncreasedSpeed = SelectedSecond = Charged = false;
        lastSpeed = speed; castAtStart = casts; parryAtStart = parries; choicesAtStart = choices;
        boostSeconds = 0;
        StoppedWhileGliding = false;
        recoverySeconds = 0f;
        wasInRecoveryArea = recoveredMana = recoveredDash = false;
    }

    public bool ObserveManaRecovery(bool inside, float mana, float dash, float deltaTime)
    {
        if (!inside)
        { recoverySeconds = 0f; wasInRecoveryArea = recoveredMana = recoveredDash = false; return false; }
        if (wasInRecoveryArea)
        {
            recoveredMana |= mana > lastMana + .001f;
            recoveredDash |= dash > lastDash + .001f;
            if (deltaTime > 0f) recoverySeconds += deltaTime;
        }
        lastMana = mana; lastDash = dash; wasInRecoveryArea = true;
        return recoverySeconds >= .75f && recoveredMana && recoveredDash;
    }

    public bool StoppedWhileGliding { get; private set; }
    public bool ObserveCruise(bool gliding, float actualSpeed)
    {
        if (gliding && actualSpeed <= .05f) StoppedWhileGliding = true;
        return StoppedWhileGliding && !gliding && actualSpeed > 1f;
    }

    public bool Observe(CatTrainingAction action, int speed, int slot, bool locked, int casts, int parries,
        int choices, bool boosting, bool braking, bool emoteAccepted, float deltaTime)
    {
        switch (action)
        {
            case CatTrainingAction.SpeedUpAndDown:
                IncreasedSpeed |= speed > lastSpeed;
                bool slowed = IncreasedSpeed && speed < lastSpeed;
                lastSpeed = speed;
                return slowed;
            case CatTrainingAction.AimAndFire:
                Charged |= locked;
                return Charged && casts > castAtStart;
            case CatTrainingAction.SwitchSlots:
                SelectedSecond |= slot == 2;
                return SelectedSecond && slot == 1;
            case CatTrainingAction.Parry: return parries > parryAtStart;
            case CatTrainingAction.ChoosePerk: return choices > choicesAtStart;
            case CatTrainingAction.Boost:
                if (boosting && deltaTime > 0f) boostSeconds += deltaTime;
                return boostSeconds >= .75f;
            case CatTrainingAction.QuickBrake: return braking;
            case CatTrainingAction.Emote: return emoteAccepted;
            default: return false;
        }
    }
}
