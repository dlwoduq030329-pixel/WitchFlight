public partial class Player
{
    // Ice's slow is separate from Curse/Storm slow, which share the HUD category.
    internal bool HasIceSlowAudio => TimerIsActive(SlowTimer);
}
