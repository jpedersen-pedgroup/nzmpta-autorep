import { describe, it, expect } from "vitest";
import { statusText } from "./AppStatus";

describe("statusText — the header indicator", () => {
  it("is a quiet 'Online' when everything has been sent", () => {
    expect(statusText("online", 0).label).toBe("Online");
    expect(statusText("online", null).label).toBe("Online");
  });

  it("counts what is still to send while online", () => {
    expect(statusText("online", 2).label).toBe("Online · 2 to send");
    expect(statusText("online", 1).detail).toBe("Connected. 1 test on this device hasn't been sent yet.");
  });

  it("says offline, and how much work is only on this device", () => {
    expect(statusText("offline", 0).label).toBe("Offline");
    expect(statusText("offline", 3).label).toBe("Offline · 3 unsent");
    expect(statusText("offline", 3).detail).toContain("3 tests are saved only on this device");
  });

  it("tells a signed-out tester that their work is safe and what to do", () => {
    expect(statusText("signed-out", 0).label).toBe("Signed out");
    const withWork = statusText("signed-out", 1);
    expect(withWork.label).toBe("Signed out · 1 unsent");
    expect(withWork.detail).toContain("Sign in again to sync");
    expect(withWork.detail).toContain("1 test on this device");
  });
});
