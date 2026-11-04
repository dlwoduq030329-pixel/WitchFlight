// Observes accepted gameplay state, never raw button presses. Reset for every exercise.
public sealed class TrainingPracticeProgress
{
    public bool IncreasedSpeed { get; private set; }
    public bool SelectedSecond { get; private set; }
    public bool Charged { get; private set; }
    private int lastSpeed, castAtStart, parryAtStart, choicesAtStart;
    private float boostSeconds;

    public void Reset(int speed, int casts, int parries, int choices)
    {
        IncreasedSpeed = SelectedSecond = Charged = false;
        lastSpeed = speed; castAtStart = casts; parryAtStart = parries; choicesAtStart = choices;
        boostSeconds = 0;
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
