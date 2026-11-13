import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { userInitials } from "./initials";

// The header's initials, pinned to the shared fixtures (tests/fixtures/initials): the same JSON
// UserInitialsTests runs against Domain/UserInitials.cs, so the offline shell and the server-rendered
// header draw the same letters.
interface Case {
  name: string;
  displayName: string | null;
  userName: string | null;
  expected: string;
}

const cases = JSON.parse(
  readFileSync(join(dirname(fileURLToPath(import.meta.url)), "../../../../tests/fixtures/initials/cases.json"), "utf8"),
) as Case[];

describe("userInitials", () => {
  it("has cases", () => expect(cases.length).toBeGreaterThan(0));
  it.each(cases)("$name", (c) => {
    expect(userInitials(c.displayName, c.userName)).toBe(c.expected);
  });
});
