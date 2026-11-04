using Fusion;

public partial class Player
{
    [Networked] public int CatHitSequence { get; private set; }
    [Networked] public int CatKillSequence { get; private set; }
    private void RecordCatAttack(bool lethal)
    {
        if (!Object.HasStateAuthority) return;
        if (lethal) CatKillSequence++; else CatHitSequence++;
    }
}
