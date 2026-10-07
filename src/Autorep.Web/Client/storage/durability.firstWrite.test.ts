// Own file: requestPersistentStorage asks at most once per page (module state), and these cases need
// a page that hasn't asked yet.
import { describe, it, expect, afterEach, vi } from "vitest";
import { protectStorageOnFirstWrite } from "./durability";
import { TESTS_CHANGED_EVENT } from "../db/testStore";

const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, "navigator");
afterEach(() => {
  if (originalNavigator) Object.defineProperty(globalThis, "navigator", originalNavigator);
});

// Codex review of #76: on a fresh device the startup check finds nothing to protect, and the
// first capture is written later — that write is the moment to ask.
describe("protectStorageOnFirstWrite", () => {
  it("asks for persistent storage when the first test is written, and only then", async () => {
    const persist = vi.fn(async () => true);
    Object.defineProperty(globalThis, "navigator", {
      value: { storage: { persisted: async () => false, persist } },
      configurable: true,
      writable: true,
    });
    const page = new EventTarget();

    protectStorageOnFirstWrite(page);
    expect(persist).not.toHaveBeenCalled();

    page.dispatchEvent(new Event(TESTS_CHANGED_EVENT));
    page.dispatchEvent(new Event(TESTS_CHANGED_EVENT));
    await new Promise((r) => setTimeout(r, 0));

    expect(persist).toHaveBeenCalledTimes(1);
  });
});
