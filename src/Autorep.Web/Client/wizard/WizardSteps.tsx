// The body of each wizard step, independent of the layout that frames it. Every shell (rail,
// single-scroll, task hub) renders steps through renderStep(), so data entry is defined once and
// switching layout can never change what a step collects — only where it sits on screen.
import type { VNode } from "preact";
import type { LocalTest, FarmSnapshot } from "../db/testStore";
import type {
  MachineConfiguration,
  VisualFaultEntry,
  WizardPlan,
  WizardStep,
} from "./types";
import { MachineConfigStep } from "./MachineConfigStep";
import { ReadingsStep } from "./ReadingsStep";
import { VisualFaultsStep } from "./VisualFaultsStep";
import { PulsatorStep } from "./PulsatorStep";
import { ClusterStep } from "./ClusterStep";
import { FaultSummaryStep } from "./FaultSummaryStep";
import { ReviewSignOffStep } from "./ReviewSignOffStep";
import { CalibrationPanel } from "../ui/CalibrationPanel";
import { additionalTestSections, testRecordSections } from "../passfail/standards";
import { preStartSections, runningSectionsFor, type ChecklistSection } from "./visualChecklist";
import { runningSectionKeys } from "./wizardProgress";
import { formatDisplayDate, type CalibrationDates } from "../calibration/status";

/** Everything a step body needs from the wizard. Built once by WizardApp and handed to whichever
 * shell is active. */
export interface StepContext {
  test: LocalTest;
  readonly: boolean;
  plan: WizardPlan;
  completed: Set<WizardStep>;
  /** Set when viewing a test from the server (admin, or a company colleague's) — read-only. */
  serverTestId?: string;
  colleagueName: string | null;
  syncing: boolean;
  generating: boolean;
  calDates: CalibrationDates | null;
  onCalDatesChanged(dates: CalibrationDates): void;
  setConfig(patch: Partial<MachineConfiguration>): void;
  setVisualFault(key: string, entry: VisualFaultEntry | null): void;
  setReading(key: string, value: number | null): void;
  setRecommendation(key: string, value: string): void;
  setDataField(key: string, value: string): void;
  checkAllSection(step: WizardStep, section: ChecklistSection): void;
  persistEdit(patch: Partial<LocalTest>): void;
  onMarkComplete(): void;
  onResync(): void;
  onDownloadReport(): void;
  onAttachPdf(file: File): void;
  onRemovePdf(): void;
}

export function renderStep(ctx: StepContext, step: WizardStep): VNode {
  const { test, readonly } = ctx;
  const attestedSectionsFor = (s: WizardStep) =>
    test.attestations.filter((a) => a.step === s && a.section).map((a) => a.section!);

  switch (step) {
    case "Setup":
      return <SetupStep ctx={ctx} />;

    case "MachineConfiguration":
      return <MachineConfigStep config={test.config} onChange={(patch) => ctx.setConfig(patch)} />;

    case "VisualFaultsPreStart": {
      const sections = preStartSections(test.config.hasReleaserPump);
      return (
        <VisualFaultsStep
          title="Visual faults — pre-start"
          sections={sections}
          entries={test.visualFaults}
          onSetEntry={(k, e) => ctx.setVisualFault(k, e)}
          onCheckAll={(secKey) => {
            const sec = sections.find((s) => s.key === secKey);
            if (sec) ctx.checkAllSection("VisualFaultsPreStart", sec);
          }}
          attestedSections={attestedSectionsFor("VisualFaultsPreStart")}
          dataValues={test.dataFields ?? {}}
          onSetData={(k, v) => ctx.setDataField(k, v)}
        />
      );
    }

    case "VisualFaultsRunning": {
      // Derived from the config, not from whichever step happens to be current — the scroll
      // layout renders every step at once, so there is no single "current step" to read from.
      const sections = runningSectionsFor(runningSectionKeys(test.config));
      return (
        <VisualFaultsStep
          title="Visual faults — running"
          sections={sections}
          entries={test.visualFaults}
          onSetEntry={(k, e) => ctx.setVisualFault(k, e)}
          onCheckAll={(secKey) => {
            const sec = sections.find((s) => s.key === secKey);
            if (sec) ctx.checkAllSection("VisualFaultsRunning", sec);
          }}
          attestedSections={attestedSectionsFor("VisualFaultsRunning")}
          dataValues={test.dataFields ?? {}}
          onSetData={(k, v) => ctx.setDataField(k, v)}
          guards={{
            value: test.guardsOnPulsators ?? false,
            onChange: (v) => ctx.persistEdit({ guardsOnPulsators: v }),
          }}
        />
      );
    }

    case "TestRecord":
      return (
        <ReadingsStep
          title="Test Record"
          hint="Enter readings — pass/fail is live against the standard for this machine."
          sections={testRecordSections(test.config, test.readings)}
          readings={test.readings}
          onSetReading={(k, v) => ctx.setReading(k, v)}
          readonly={readonly}
          storedVerdicts={test.verdicts}
        />
      );

    case "AdditionalTests":
      return (
        <ReadingsStep
          title="Additional Tests"
          hint="Only the sections relevant to this machine's ancillaries are shown."
          sections={additionalTestSections(test.config, test.readings)}
          readings={test.readings}
          onSetReading={(k, v) => ctx.setReading(k, v)}
          readonly={readonly}
          storedVerdicts={test.verdicts}
        />
      );

    case "PulsatorTest":
      return (
        <PulsatorStep
          config={test.config}
          rows={test.pulsatorRows ?? []}
          onRows={(rows) => ctx.persistEdit({ pulsatorRows: rows })}
          readings={test.readings}
          onSetReading={(k, v) => ctx.setReading(k, v)}
          readonly={readonly}
          storedVerdicts={test.verdicts}
        />
      );

    case "IndividualClusterTest":
      return (
        <ClusterStep
          config={test.config}
          rows={test.clusterRows ?? []}
          onRows={(rows) => ctx.persistEdit({ clusterRows: rows })}
          readonly={readonly}
        />
      );

    case "FaultSummary":
      return <FaultSummaryStep test={test} onSetRecommendation={(k, v) => ctx.setRecommendation(k, v)} />;

    case "ReviewSignOff":
      return (
        <ReviewSignOffStep
          test={test}
          steps={ctx.plan.steps}
          completed={ctx.completed}
          syncing={ctx.syncing}
          generating={ctx.generating}
          isServerView={Boolean(ctx.serverTestId)}
          colleagueName={ctx.colleagueName}
          onMarkComplete={() => ctx.onMarkComplete()}
          onResync={() => ctx.onResync()}
          onDownloadReport={() => ctx.onDownloadReport()}
          onAttachPdf={(file) => ctx.onAttachPdf(file)}
          onRemovePdf={() => ctx.onRemovePdf()}
        />
      );
  }
}

function SetupStep({ ctx }: { ctx: StepContext }) {
  const { test, readonly } = ctx;
  return (
    <>
      <div class="card">
        <div class="card__title">Farm &amp; details</div>
        <div class="form-grid">
          {farmField("Farm", test.farm?.name ?? test.farmName)}
          {farmField("Supply number", test.farm?.supplyNumber)}
          {farmField("Milk supply company", test.farm?.milkCompanyName)}
          {farmField("Region", test.farm?.regionName)}
          {farmField("Address", farmAddress(test.farm))}
          {farmField("RAPID number", test.farm?.rapidNumber)}
          {farmField("Farmer", test.farm?.farmerName)}
          {farmField("Phone", test.farm?.contactPhone)}
          {farmField("Email", test.farm?.contactEmail)}
        </div>
        <p style="color:var(--text-muted);font-size:0.8125rem;margin-top:var(--space-4)">
          Farm details are managed in the admin area.
        </p>
      </div>

      {/* Calibration belongs to the TESTER, not this farm/test. Editable tests show the live
          profile panel (edits update the profile and every future test); completed and migrated
          tests show the snapshot frozen into the record at sign-off. */}
      {readonly ? (
        <div class="card">
          <div class="card__title">
            Calibration expiry dates{" "}
            <small class="card__hint">The tester's equipment, as recorded for this test.</small>
          </div>
          <div class="form-grid">
            {farmField("Air-flow meters", calSnapshot(test.calAirFlowMeters))}
            {farmField("Pulsator testers", calSnapshot(test.calPulsatorTesters))}
            {farmField("Vacuum gauges", calSnapshot(test.calVacuumGauges))}
          </div>
        </div>
      ) : (
        <CalibrationPanel onChanged={ctx.onCalDatesChanged} />
      )}
    </>
  );
}

function farmField(label: string, value?: string | null) {
  return (
    <div>
      <span style="color:var(--text-muted);font-size:0.8125rem">{label}</span>
      <div>{value && value.length > 0 ? value : "—"}</div>
    </div>
  );
}

function calSnapshot(iso: string | null | undefined): string | null {
  return iso ? formatDisplayDate(iso) : null;
}

function farmAddress(f?: FarmSnapshot): string | null {
  if (!f) return null;
  const parts = [f.addressLine1, f.addressLine2, f.town, f.postCode].filter((p): p is string => Boolean(p));
  return parts.length > 0 ? parts.join(", ") : null;
}
