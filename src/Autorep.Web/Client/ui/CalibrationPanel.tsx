// The tester's equipment-calibration panel, shown on "My tests" and the wizard Setup step.
// The dates belong to the tester's PROFILE (their instruments travel with them farm to farm),
// not to a farm or test: an edit here updates the profile — offline-safe via the dirty cache —
// and applies to every future test. Renewal highlighting: amber from 6 weeks out, red once
// expired. Expired equipment warns the tester but NEVER blocks starting or completing a test.
import { useEffect, useState } from "preact/hooks";
import { DatePicker } from "./DatePicker";
import { showToast } from "./toast";
import { getCachedCalibration, saveCalibration } from "../sync/calibrationSync";
import {
  describeStatus,
  equipmentStatuses,
  todayIso,
  worstState,
  type CalibrationDates,
  type CalibrationState,
} from "../calibration/status";

function badgeClass(state: CalibrationState): string {
  switch (state) {
    case "expired":
      return "badge badge--danger";
    case "due":
      return "badge badge--warning";
    case "ok":
      return "badge badge--success";
    default:
      return "badge";
  }
}

/** The page-level warning strip. Renders nothing while everything is fine — and it only ever
 * warns; testing is never blocked on calibration. */
export function CalibrationAlert({ dates }: { dates: CalibrationDates }) {
  const today = todayIso();
  const worst = worstState(dates, today);
  if (worst !== "expired" && worst !== "due") return null;

  const flagged = equipmentStatuses(dates, today).filter((s) => s.state === worst);
  const items = flagged.map((s) => `${s.label} (${describeStatus(s)})`).join(" · ");

  if (worst === "expired") {
    return (
      <div class="alert alert--danger">
        ⛔ <strong>Equipment calibration expired</strong> — {items}. You can still complete
        tests, but arrange recalibration now.
      </div>
    );
  }
  return (
    <div class="alert alert--warning">
      ⚠️ <strong>Equipment calibration due for renewal</strong> — {items}.
    </div>
  );
}

interface Props {
  /** Fires with the new dates on every edit so the host page can refresh its own banner. */
  onChanged?: (dates: CalibrationDates) => void;
}

export function CalibrationPanel({ onChanged }: Props) {
  const [dates, setDates] = useState<CalibrationDates | null>(null);

  useEffect(() => {
    void getCachedCalibration().then(setDates);
  }, []);

  if (!dates) return null;

  const set = async (key: keyof CalibrationDates, value: string | null) => {
    const next = { ...dates, [key]: value };
    setDates(next);
    onChanged?.(next);
    if ((await saveCalibration(next)) === "offline") {
      showToast("Calibration date saved on this device — it will sync when you're back online.", "info");
    }
  };

  const today = todayIso();
  const statuses = equipmentStatuses(dates, today);

  return (
    <div class="card">
      <div class="card__title">
        Your equipment calibration{" "}
        <small class="card__hint">
          These follow you, not the farm — expiry dates for your own air-flow meters, pulsator
          testers and vacuum gauges.
        </small>
      </div>
      <CalibrationAlert dates={dates} />
      <div class="form-grid">
        {statuses.map((s) => (
          <div class="form-field" key={s.key}>
            <label>
              {s.label}{" "}
              {s.state !== "unknown" && <span class={badgeClass(s.state)}>{describeStatus(s)}</span>}
            </label>
            <DatePicker value={dates[s.key]} onChange={(v) => void set(s.key, v)} />
          </div>
        ))}
      </div>
    </div>
  );
}
