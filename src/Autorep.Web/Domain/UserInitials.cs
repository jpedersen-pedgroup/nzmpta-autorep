namespace Autorep.Web.Domain;

/// <summary>
/// The initials in the header's account circle (Josh, 13 Nov 2026): the first letters of the first
/// and last words of the person's name, so "Josh Pedersen" is "JP". A one-word name gives one letter,
/// and an account with no name falls back to the first letter of its sign-in email. The offline app
/// shell draws the same with Client/shell/initials.ts; keep the two together.
/// </summary>
public static class UserInitials
{
    /// <summary>The person's name (<c>Tester.DisplayName</c>), stamped on the sign-in by
    /// <c>TesterClaimsPrincipalFactory</c> and refreshed with the security stamp (every minute).</summary>
    public const string DisplayNameClaim = "autorep:display_name";

    public static string For(string? displayName, string? userName)
    {
        var words = (displayName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(FirstLetter)
            .OfType<char>()
            .ToList();
        if (words.Count > 0)
            return words.Count == 1 ? $"{words[0]}" : $"{words[0]}{words[^1]}";
        return FirstLetter(userName ?? "") is { } c ? $"{c}" : "";
    }

    // The first letter or digit, so "(Jono)" gives J and "O'Brien" gives O.
    private static char? FirstLetter(string word)
    {
        foreach (var c in word)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c);
        return null;
    }
}
