import { describe, expect, it } from "vitest";
import { createOutboxReporter, deviceId, deviceLabel, RESEND_AFTER_MS, type OutboxDeps, type OutboxReport } from "./outboxReport";

function memoryStorage() {
  const items = new Map<string, string>();
  return { getItem: (k: string) => items.get(k) ?? null, setItem: (k: string, v: string) => void items.set(k, v) };
}

function harness(over: Partial<OutboxDeps> = {}) {
  const sent: OutboxReport[] = [];
  const state = { count: 2, online: true, now: 1_000, accept: true, owner: "tester-1" as string | null | undefined };
  const report = createOutboxReporter({
    count: async () => state.count,
    owner: () => state.owner,
    online: () => state.online,
    send: async (r) => {
      if (!state.accept) return false;
      sent.push(r);
      return true;
    },
    deviceId: () => "device-1",
    label: () => "iPad · Safari",
    now: () => state.now,
    ...over,
  });
  return { report, sent, state };
}

describe("deviceId", () => {
  it("is made once and then kept", () => {
    const storage = memoryStorage();
    const first = deviceId(storage);
    expect(first).toMatch(/^[0-9a-f-]{36}$/);
    expect(deviceId(storage)).toBe(first);
  });

  it("is null when nothing can be kept", () => {
    expect(deviceId(null)).toBeNull();
    const refusing = { getItem: () => null, setItem: () => { throw new Error("quota"); } };
    expect(deviceId(refusing)).toBeNull();
  });
});

describe("the outbox report", () => {
  it("says how many tests the server hasn't got, for the store's tester", async () => {
    const { report, sent } = harness();
    expect(await report()).toBe("sent");
    expect(sent).toEqual([{ deviceId: "device-1", testerId: "tester-1", unsentTests: 2, device: "iPad · Safari" }]);
  });

  it("waits while offline, and for a device it can't name", async () => {
    const offline = harness();
    offline.state.online = false;
    expect(await offline.report()).toBe("skipped");

    const anonymous = harness({ deviceId: () => null });
    expect(await anonymous.report()).toBe("skipped");
    expect(anonymous.sent).toEqual([]);

    const legacyStore = harness();
    legacyStore.state.owner = null;
    expect(await legacyStore.report()).toBe("skipped");
  });

  it("sends a change at once, and an unchanged count only after a while", async () => {
    const { report, sent, state } = harness();
    await report();
    expect(await report()).toBe("unchanged");

    state.count = 0; // a sync sent them
    expect(await report()).toBe("sent");

    state.now += RESEND_AFTER_MS;
    expect(await report()).toBe("sent");
    expect(sent.map((r) => r.unsentTests)).toEqual([2, 0, 0]);
  });

  it("tries again next time when the server didn't take it", async () => {
    const { report, sent, state } = harness();
    state.accept = false;
    expect(await report()).toBe("skipped");
    state.accept = true;
    expect(await report()).toBe("sent");
    expect(sent).toHaveLength(1);
  });

  it("reports afresh for another tester's store on the same device", async () => {
    const { report, sent, state } = harness();
    await report();
    state.owner = "tester-2";
    expect(await report()).toBe("sent");
    expect(sent.map((r) => r.testerId)).toEqual(["tester-1", "tester-2"]);
  });
});

describe("deviceLabel", () => {
  it("tells an administrator what kind of device it is", () => {
    const ipadSafari = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15";
    expect(deviceLabel(ipadSafari, 5)).toBe("iPad · Safari");
    expect(deviceLabel(ipadSafari, 0)).toBe("Mac · Safari");
    expect(deviceLabel("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36 Edg/129.0")).toBe("Windows PC · Edge");
    expect(deviceLabel("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Mobile Safari/537.36")).toBe("Android phone · Chrome");
    expect(deviceLabel("curl/8.4")).toBe("Device");
  });
});
