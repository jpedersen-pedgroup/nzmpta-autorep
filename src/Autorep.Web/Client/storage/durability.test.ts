import { describe, it, expect, afterEach, vi } from "vitest";
import { describeStorage, formatBytes, requestPersistentStorage, storageReport } from "./durability";
import { isQuotaError } from "../db/testStore";

const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, "navigator");

function stubStorage(storage: unknown) {
  Object.defineProperty(globalThis, "navigator", { value: { storage }, configurable: true, writable: true });
}

afterEach(() => {
  if (originalNavigator) Object.defineProperty(globalThis, "navigator", originalNavigator);
});

describe("storage on the device", () => {
  it("reports usage, quota and whether the browser has agreed to keep it", async () => {
    stubStorage({
      estimate: async () => ({ usage: 15 * 1024 * 1024, quota: 2 * 1024 ** 3 }),
      persisted: async () => true,
    });

    const report = await storageReport();

    expect(report).toEqual({ usage: 15 * 1024 * 1024, quota: 2 * 1024 ** 3, persisted: true });
    expect(describeStorage(report)).toBe("AutoRep is using 15.0 MB on this device of 2.0 GB available — kept until you remove it.");
  });

  it("says plainly when the device may clear it", () => {
    expect(describeStorage({ usage: 2048, quota: null, persisted: false })).toBe(
      "AutoRep is using 2.0 KB on this device — the device may clear it if space runs short, so sync often.",
    );
  });

  it("says nothing when the browser can't say", async () => {
    stubStorage(undefined);
    expect(describeStorage(await storageReport())).toBeNull();
  });

  it("asks for persistence at most once a page, and not at all when it's already granted", async () => {
    const persist = vi.fn(async () => false);
    stubStorage({ persisted: async () => false, persist });

    expect(await requestPersistentStorage()).toBe(false);
    expect(await requestPersistentStorage()).toBe(false);
    expect(persist).toHaveBeenCalledTimes(1);

    const persistAgain = vi.fn(async () => true);
    stubStorage({ persisted: async () => true, persist: persistAgain });
    expect(await requestPersistentStorage()).toBe(true);
    expect(persistAgain).not.toHaveBeenCalled();
  });

  it("is null where the browser has no such thing", async () => {
    stubStorage(undefined);
    expect(await requestPersistentStorage()).toBeNull();
  });
});

describe("formatBytes", () => {
  it("reads like a person would say it", () => {
    expect(formatBytes(512)).toBe("512 bytes");
    expect(formatBytes(1536)).toBe("1.5 KB");
    expect(formatBytes(150 * 1024 * 1024)).toBe("150 MB");
  });
});

describe("isQuotaError — a write refused for want of space", () => {
  it("recognises the browsers' ways of saying so", () => {
    expect(isQuotaError(new DOMException("full", "QuotaExceededError"))).toBe(true);
    expect(isQuotaError({ name: "NS_ERROR_DOM_QUOTA_REACHED" })).toBe(true);
    expect(isQuotaError({ code: 22 })).toBe(true);
    expect(isQuotaError({ name: "AbortError", inner: { name: "QuotaExceededError" } })).toBe(true);
  });

  it("doesn't mistake anything else for it", () => {
    expect(isQuotaError(new TypeError("nope"))).toBe(false);
    expect(isQuotaError(null)).toBe(false);
  });
});
