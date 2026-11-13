/**
 * The initials in the header's account circle: the first letters of the first and last words of the
 * person's name, so "Josh Pedersen" is "JP". A one-word name gives one letter, and an account with no
 * name falls back to the first letter of its sign-in email. Mirrors Domain/UserInitials.cs, which
 * draws the server-rendered header; keep the two together.
 */
export function userInitials(displayName: string | null | undefined, userName: string | null | undefined): string {
  const letters = (displayName ?? "")
    .split(/\s+/)
    .map(firstLetter)
    .filter((c): c is string => c !== null);
  if (letters.length > 0) return letters.length === 1 ? letters[0] : letters[0] + letters[letters.length - 1];
  return firstLetter(userName ?? "") ?? "";
}

/** The first letter or digit, so "(Jono)" gives J and "O'Brien" gives O. */
function firstLetter(word: string): string | null {
  for (const c of word) if (/[\p{L}\p{Nd}]/u.test(c)) return c.toUpperCase();
  return null;
}
