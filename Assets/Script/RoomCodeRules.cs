/// <summary>Shared by room creation, joining and their UI. Random matchmaking has no code.</summary>
public static class RoomCodeRules
{
    public const int Length = 4;
    public const string Hint = "비밀번호는 공백 없이 정확히 4글자로 입력해주세요.";

    public static bool TryValidate(string input, out string code, out string error)
    {
        code = input == null ? string.Empty : input.Trim();
        error = Hint;
        if (code.Length != Length) return false;
        foreach (char character in code)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || char.IsSurrogate(character))
                return false;
        }
        error = null;
        return true;
    }
}
