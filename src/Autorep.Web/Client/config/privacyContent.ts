// Admin-managed privacy content (terms version, IPP3A collection notice, report footer line,
// privacy contact + statement URL), synced from /api/privacy and cached in IndexedDB so the report
// footer + in-app notices work offline. A built-in default keeps a sensible footer when nothing has
// synced yet (e.g. first run, or offline before the first sync).

export interface PrivacyContentData {
  termsVersion: string;
  collectionNotice: string;
  reportFooterText: string;
  privacyContactEmail: string;
  privacyStatementUrl: string;
}

const DEFAULT: PrivacyContentData = {
  termsVersion: "",
  collectionNotice: "",
  reportFooterText:
    "Personal information in this report is held under the Privacy Act 2020 for milking-machine " +
    "test reporting and compliance.",
  privacyContactEmail: "",
  privacyStatementUrl: "",
};

let current: PrivacyContentData = DEFAULT;

export function applyPrivacyContent(data: Partial<PrivacyContentData> | null | undefined): void {
  if (!data) return;
  current = { ...DEFAULT, ...current, ...data };
}

export function getPrivacyContent(): PrivacyContentData {
  return current;
}
