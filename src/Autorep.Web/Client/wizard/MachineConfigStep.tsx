// Machine Configuration & Ancillary step — the legacy "Farm & Milking Details" machine fields.
// Grouped into Plant / Pulsation / Cluster & liners / Ancillary tabs. Dropdowns are backed by the
// reference lists pulled from the legacy Lookup / AtmosPressure / Pulsator tables.
import type { ComponentChildren } from "preact";
import type { MachineConfiguration, PlantType, PumpLubrication } from "./types";
import { Tabs } from "../ui/Tabs";
import { Combobox } from "../ui/Combobox";
import {
  ATMOS_PRESSURES,
  LINERS,
  MILKLINE_SIZES,
  PULSATOR_BRANDS,
  PULSATOR_CONFIGS,
  SHELLS,
  pulsatorModelsForBrand,
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

export function MachineConfigStep({ config, onChange }: Props) {
  const tabs = [
    {
      key: "plant",
      label: "Plant",
      content: (
        <div class="form-grid">
          <Field label="Plant type">
            <select
              value={config.plantType}
              onChange={(e) => onChange({ plantType: (e.currentTarget as HTMLSelectElement).value as PlantType })}
            >
              <option value="HerringboneLowline">Herringbone (lowline)</option>
              <option value="HerringboneHighline">Herringbone (highline)</option>
              <option value="Rotary">Rotary</option>
              <option value="Other">Other</option>
            </select>
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
              options={MILKLINE_SIZES}
              listId="cfg-milkline"
            />
          </Field>
          <Field label="Atmospheric pressure (kPa)">
            <select
              value={config.atmosPressureSeaLevel ?? ""}
              onChange={(e) => {
                const v = (e.currentTarget as HTMLSelectElement).value;
                onChange({ atmosPressureSeaLevel: v === "" ? null : Number(v) });
              }}
            >
              <option value="">— select —</option>
              {ATMOS_PRESSURES.map((a) => (
                <option key={a.kpa} value={a.kpa}>
                  {a.kpa} kPa
                </option>
              ))}
            </select>
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
            <select
              value={config.pumpLubrication}
              onChange={(e) =>
                onChange({ pumpLubrication: (e.currentTarget as HTMLSelectElement).value as PumpLubrication })
              }
            >
              <option value="OilLubricated">Oil lubricated</option>
              <option value="LiquidRing">Liquid ring</option>
              <option value="Other">Other</option>
            </select>
          </Field>
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
                const models = pulsatorModelsForBrand(v);
                onChange({ pulsatorBrand: v, pulsatorModel: models.length === 1 ? models[0] : null });
              }}
              options={PULSATOR_BRANDS}
              listId="cfg-pulsator-brand"
            />
          </Field>
          <Field label="Pulsator type">
            <Combobox
              value={config.pulsatorModel}
              onChange={(v) => onChange({ pulsatorModel: v })}
              options={pulsatorModelsForBrand(config.pulsatorBrand)}
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
              options={PULSATOR_CONFIGS}
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
            <Combobox value={config.shellModel} onChange={(v) => onChange({ shellModel: v })} options={SHELLS} listId="cfg-shell" />
          </Field>
          <Field label="Front liner">
            <Combobox value={config.linerModel} onChange={(v) => onChange({ linerModel: v })} options={LINERS} listId="cfg-front-liner" />
          </Field>
          <Field label="Back liner">
            <Combobox value={config.backLiner} onChange={(v) => onChange({ backLiner: v })} options={LINERS} listId="cfg-back-liner" />
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
