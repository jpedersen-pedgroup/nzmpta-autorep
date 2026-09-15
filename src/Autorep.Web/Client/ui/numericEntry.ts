// What a keystroke in a numeric field means for the stored value. Split out from DecimalInput so
// the rules are unit-tested without a DOM.
//
// A number input bound straight to its stored value fights the tester mid-number: "41." parses to
// 41, the parsed value is written back over the text, and Chromium drops the caret at the start —
// so the next key lands in front and "41.5" is entered as "541". DecimalInput leaves the text
// alone while the field is focused; only the decisions below reach the store.

/** The stored value after this keystroke: a number, null to clear it, or undefined to leave the
 * stored value alone (a half-typed "41." or "-", or text that parses to what is already stored). */
export function readTypedNumber(
  raw: string,
  badInput: boolean,
  stored: string,
): number | null | undefined {
  if (raw === "") {
    // Chromium and Safari sanitise a half-typed number ("41.", "-") to "" and flag badInput.
    // Clearing the reading there is what deleted it as the tester typed the decimal point.
    if (badInput) return undefined;
    return stored === "" ? undefined : null;
  }
  const n = Number(raw.replace(",", "."));
  if (!Number.isFinite(n)) return undefined;
  // "41.0" parses to 41: storing it again would rewrite the field and strip the "0", so a tester
  // could never reach 41.05. Nothing changed, so nothing is stored.
  return String(n) === stored ? undefined : n;
}
