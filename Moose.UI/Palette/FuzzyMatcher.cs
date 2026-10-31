namespace Moose.UI.Palette;

static class FuzzyMatcher
{
    public static int? Score(string query, string text)
    {
        var penalty = 0;
        var position = 0;
        foreach (var character in query)
        {
            if (char.IsWhiteSpace(character)) continue;
            var index = text.IndexOf(character.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            if (index != position || position == 0) penalty += IsWordStart(text, index) ? 1 : 2 + index - position;
            position = index + 1;
        }
        return penalty;
    }

    static bool IsWordStart(string text, int index) => index == 0 || !char.IsLetterOrDigit(text[index - 1]);
}
