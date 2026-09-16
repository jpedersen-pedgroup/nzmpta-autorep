// Machine Configuration & Ancillary step — the legacy "Farm & Milking Details" machine fields.
// Grouped into Plant / Pulsation / Cluster & liners / Ancillary tabs. Dropdowns are backed by the
// reference lists pulled from the legacy Lookup / AtmosPressure / Pulsator tables.
import type { ComponentChildren } from "preact";
import type {
  MachineConfiguration,
  PlantType,
  PumpLubrication,
  ReleaserPumpDetail,
  VacuumPumpDetail,
} from "./types";
import { releaserPumpRows, vacuumPumpRows, withRow } from "./pumpRows";
import { PLANT_LABELS, PUMP_LUBRICATION_LABELS } from "./configLabels";
import { Tabs } from "../ui/Tabs";
import { Combobox } from "../ui/Combobox";
import { Select } from "../ui/Select";
import {
  ATMOS_PRESSURES,
  linerOptions,
  milklineSizeOptions,
  pulsatorBrandOptions,
  pulsatorConfigOptions,
  pulsatorModelOptionsForBrand,
  regulatorTypeOptions,
  releaserPumpMakeOptions,
  releaserPumpModelOptionsForMake,
  shellOptions,
  vacuumPumpMakeOptions,
  vacuumPumpModelOptionsForMake,
} from "../reference/lookups";

interface Props {
  config: MachineConfiguration;
  onChange: (patch: Partial<MachineConfiguration>) => void;
}

function Field({ label, children }: { label: string; children: ComponentChildren }) {
  return (
    <div class="form-field">
      <label>{label}</label>
      {children}
    </div>
  );
}

function TextInput({
  value,
  onInput,
  placeholder,
}: {
  value?: string | null;
  onInput: (v: string | null) => void;
  placeholder?: string;
}) {
  return (
    <input
      type="text"
      placeholder={placeholder}
      value={value ?? ""}
      onInput={(e) => {
        const v = (e.currentTarget as HTMLInputElement).value;
        onInput(v.trim() === "" ? null : v);
      }}
    />
  );
}

function NumberInput({
  value,
  onInput,
  min,
}: {
  value?: number | null;
  onInput: (v: number | null) => void;
  min?: number;
}) {
  return (
    <input
      type="number"
      min={min}
      value={value ?? ""}
      onInput={(e) => {
        const v = (e.currentTarget as HTMLInputElement).value;
        onInput(v === "" ? null : Number(v));
      }}
    />
  );
}

function Toggle({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <label class="form-check">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange((e.currentTarget as HTMLInputElement).checked)}
      />
      <span>{label}</span>
    </label>
  );
}

/** One pump's fields, boxed so a plant with several pumps reads as several pumps. */
function PumpRow({
  title,
  onRemove,
  children,
}: {
  title: string;
  onRemove?: () => void;
  children: ComponentChildren;
}) {
  return (
    <fieldset class="pump-row">
      <legend>
        {title}
        {onRemove && (
          <button type="button" class="btn btn--secondary btn--sm pump-row__remove" onClick={onRemove}>
            Remove
          </button>
        )}
      </legend>
      <div class="form-grid">{children}</div>
    </fieldset>
  );
}

/** Make -> model, as the pulsator fields already work: a make with exactly one model fills it in,
 * and a model that doesn't belong to the new make is cleared. A make typed by hand has no
 * catalogue models, so whatever the tester typed as the model stands. */
function modelForMake(models: string[], current?: string | null): string | null {
  if (models.length === 1) return models[0];
  if (current && (models.length === 0 || models.includes(current))) return current;
  return null;
}

/** One row per vacuum pump, following the pump count - the make/model/motor/regulator the legacy
 * app captured on page 2, and what a replacement quote needs (tester feedback, 15 Sep 2026). */
function VacuumPumpRowsEditor({ config, onChange }: Props) {
  const rows = vacuumPumpRows(config);
  const set = (i: number, patch: Partial<VacuumPumpDetail>) =>
    onChange({ vacuumPumps: withRow(config.vacuumPumps, i, patch) });
  return (
    <div class="form-field--full">
      <label class="pump-rows__label">Vacuum pump details</label>
      <p class="form-field__hint">One row per pump, following the pump count above.</p>
      {rows.map((row, i) => (
        <PumpRow key={`vp-${i}`} title={rows.length > 1 ? `Pump ${i + 1}` : "Vacuum pump"}>
          <Field label="Make">
            <Combobox
              value={row.make}
              onChange={(v) => set(i, { make: v, model: modelForMake(vacuumPumpModelOptionsForMake(v), row.model) })}
              options={vacuumPumpMakeOptions()}
              listId={`cfg-vp-make-${i}`}
            />
          </Field>
          <Field label="Model">
            <Combobox
              value={row.model}
              onChange={(v) => set(i, { model: v })}
              options={vacuumPumpModelOptionsForMake(row.make)}
              listId={`cfg-vp-model-${i}`}
            />
          </Field>
          <Field label="Motor size (kW)">
            <TextInput value={row.motorSize} onInput={(v) => set(i, { motorSize: v })} placeholder="e.g. 7.5" />
          </Field>
          <Field label="Regulator type">
            <Combobox
              value={row.regulatorType}
              onChange={(v) => set(i, { regulatorType: v })}
              options={regulatorTypeOptions()}
              listId={`cfg-vp-reg-${i}`}
              placeholder="Type it in"
            />
          </Field>
          <Toggle
            label="Drives the milk pump"
            checked={row.drivesMilkPump ?? false}
            onChange={(v) => set(i, { drivesMilkPump: v })}
          />
        </PumpRow>
      ))}
    </div>
  );
}

/** Releaser (milk) pumps - added by hand, since nothing in the configuration counts them. */
function ReleaserPumpRowsEditor({ config, onChange }: Props) {
  const rows = releaserPumpRows(config);
  const set = (i: number, patch: Partial<ReleaserPumpDetail>) =>
    onChange({ releaserPumps: withRow(config.releaserPumps, i, patch) });
  return (
    <div class="form-field--full">
      <label class="pump-rows__label">Releaser pump details</label>
      {rows.map((row, i) => (
        <PumpRow
          key={`rp-${i}`}
          title={rows.length > 1 ? `Releaser pump ${i + 1}` : "Releaser pump"}
          onRemove={rows.length > 1 ? () => onChange({ releaserPumps: rows.filter((_, j) => j !== i) }) : undefined}
        >
          <Field label="Make">
            <Combobox
              value={row.make}
              onChange={(v) => set(i, { make: v, model: modelForMake(releaserPumpModelOptionsForMake(v), row.model) })}
              options={releaserPumpMakeOptions()}
              listId={`cfg-rp-make-${i}`}
            />
          </Field>
          <Field label="Model">
            <Combobox
              value={row.model}
              onChange={(v) => set(i, { model: v })}
              options={releaserPumpModelOptionsForMake(row.make)}
              listId={`cfg-rp-model-${i}`}
            />
          </Field>
          <Field label="Motor size (kW)">
            <TextInput value={row.motorSize} onInput={(v) => set(i, { motorSize: v })} placeholder="e.g. 2.2" />
          </Field>
        </PumpRow>
      ))}
      <button type="button" class="btn btn--secondary btn--sm" onClick={() => onChange({ releaserPumps: [...rows, {}] })}>
        + Add releaser pump
      </button>
    </div>
  );
}

export function MachineConfigStep({ config, onChange }: Props) {
  const tabs = [
    {
      key: "plant",
      label: "Plant",
      content: (
        <div class="form-grid">
          <Field label="Plant type">
            <Select
              value={config.plantType}
              onChange={(v) => onChange({ plantType: v as PlantType })}
              options={(Object.keys(PLANT_LABELS) as PlantType[]).map((value) => ({ value, label: PLANT_LABELS[value] }))}
            />
          </Field>
          <Field label="Plant size">
            <TextInput
              value={config.plantSize}
              onInput={(v) => onChange({ plantSize: v })}
              placeholder="e.g. 30 a-side"
            />
          </Field>
          <Field label="Cluster count">
            <NumberInput value={config.clusterCount} min={0} onInput={(v) => onChange({ clusterCount: v ?? 0 })} />
          </Field>
          <Field label="Herd size">
            <NumberInput value={config.herdSize} min={0} onInput={(v) => onChange({ herdSize: v })} />
          </Field>
          <Field label="Milkline size (mm)">
            <Combobox
              value={config.milklineSize}
              onChange={(v) => onChange({ milklineSize: v })}
              options={milklineSizeOptions()}
              listId="cfg-milkline"
            />
          </Field>
          <Field label="Atmospheric pressure (kPa)">
            <Select
              value={config.atmosPressureSeaLevel != null ? String(config.atmosPressureSeaLevel) : ""}
              onChange={(v) => onChange({ atmosPressureSeaLevel: v === "" ? null : Number(v) })}
              options={[
                { value: "", label: "— select —" },
                ...ATMOS_PRESSURES.map((a) => ({ value: String(a.kpa), label: `${a.kpa} kPa` })),
              ]}
            />
          </Field>
          <Field label="Last BMCC">
            <TextInput value={config.lastBmcc} onInput={(v) => onChange({ lastBmcc: v })} />
          </Field>
          <Field label="No. of vacuum pumps">
            <NumberInput
              value={config.numberOfVacuumPumps}
              min={0}
              onInput={(v) => onChange({ numberOfVacuumPumps: v ?? 0 })}
            />
          </Field>
          <Field label="Pump lubrication">
            <Select
              value={config.pumpLubrication}
              onChange={(v) => onChange({ pumpLubrication: v as PumpLubrication })}
              options={(Object.keys(PUMP_LUBRICATION_LABELS) as PumpLubrication[]).map((value) => ({
                value,
                label: PUMP_LUBRICATION_LABELS[value],
              }))}
            />
          </Field>
          <VacuumPumpRowsEditor config={config} onChange={onChange} />
        </div>
      ),
    },
    {
      key: "pulsation",
      label: "Pulsation",
      content: (
        <div class="form-grid">
          <Field label="Pulsator brand">
            <Combobox
              value={config.pulsatorBrand}
              onChange={(v) => {
                // Auto-select the type when the chosen brand has only one model.
                const models = pulsatorModelOptionsForBrand(v);
                onChange({ pulsatorBrand: v, pulsatorModel: models.length === 1 ? models[0] : null });
              }}
              options={pulsatorBrandOptions()}
              listId="cfg-pulsator-brand"
            />
          </Field>
          <Field label="Pulsator type">
            <Combobox
              value={config.pulsatorModel}
              onChange={(v) => onChange({ pulsatorModel: v })}
              options={pulsatorModelOptionsForBrand(config.pulsatorBrand)}
              listId="cfg-pulsator-type"
            />
          </Field>
          <Field label="No. of pulsators">
            <NumberInput value={config.pulsatorCount} min={0} onInput={(v) => onChange({ pulsatorCount: v ?? 0 })} />
          </Field>
          <Field label="Pulsator configuration">
            <Combobox
              value={config.pulsatorConfiguration}
              onChange={(v) => onChange({ pulsatorConfiguration: v })}
              options={pulsatorConfigOptions()}
              listId="cfg-pulsator-config"
            />
          </Field>
          <Toggle
            label="Flushing pulsation system"
            checked={config.flushingPulsationSystem}
            onChange={(v) => onChange({ flushingPulsationSystem: v })}
          />
          <Toggle
            label="Pulsator-stop system"
            checked={config.hasPulsatorStopSystem}
            onChange={(v) => onChange({ hasPulsatorStopSystem: v })}
          />
        </div>
      ),
    },
    {
      key: "cluster",
      label: "Cluster & liners",
      content: (
        <div class="form-grid">
          <Field label="Claw">
            <TextInput value={config.clawModel} onInput={(v) => onChange({ clawModel: v })} placeholder="Enter claw" />
          </Field>
          <Field label="Shell">
            <Combobox value={config.shellModel} onChange={(v) => onChange({ shellModel: v })} options={shellOptions()} listId="cfg-shell" />
          </Field>
          <Field label="Front liner">
            <Combobox value={config.linerModel} onChange={(v) => onChange({ linerModel: v })} options={linerOptions()} listId="cfg-front-liner" />
          </Field>
          <Field label="Back liner">
            <Combobox value={config.backLiner} onChange={(v) => onChange({ backLiner: v })} options={linerOptions()} listId="cfg-back-liner" />
          </Field>
          <Toggle label="Vented liners" checked={config.linerVented} onChange={(v) => onChange({ linerVented: v })} />
        </div>
      ),
    },
    {
      key: "ancillary",
      label: "Ancillary equipment",
      content: (
        <div class="form-grid">
          <Toggle label="Variable speed drive (VSD)" checked={config.vsdFitted} onChange={(v) => onChange({ vsdFitted: v })} />
          <Toggle
            label="ISO test ports available"
            checked={config.isoPortsAvailable}
            onChange={(v) => onChange({ isoPortsAvailable: v })}
          />
          <Toggle label="Automatic cluster removers (ACRs)" checked={config.hasAcr} onChange={(v) => onChange({ hasAcr: v })} />
          <Toggle label="Bail gates" checked={config.hasBailGates} onChange={(v) => onChange({ hasBailGates: v })} />
          <Toggle label="Milk meters" checked={config.hasMilkMeters} onChange={(v) => onChange({ hasMilkMeters: v })} />
          <Toggle label="Teat sprayer" checked={config.hasTeatSprayer} onChange={(v) => onChange({ hasTeatSprayer: v })} />
          <Toggle label="Backing gate" checked={config.hasBackingGate} onChange={(v) => onChange({ hasBackingGate: v })} />
          <Toggle label="Releaser pump" checked={config.hasReleaserPump} onChange={(v) => onChange({ hasReleaserPump: v })} />
          {config.hasReleaserPump && <ReleaserPumpRowsEditor config={config} onChange={onChange} />}
        </div>
      ),
    },
  ];

  return (
    <div class="card">
      <div class="card__title">
        Machine configuration{" "}
        <small class="card__hint">Changes save to this device immediately and re-resolve the steps on the left.</small>
      </div>
      <Tabs tabs={tabs} />
    </div>
  );
}
