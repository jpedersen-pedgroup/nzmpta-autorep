// Page-level events the bundle's independent Preact trees use to hear about each other's work.

/** Dispatched on window once the background reference refresh finishes (Client/main.ts). `detail`
 * says whether anything the page renders from — standards, catalogs, the farm book — changed. */
export const REFERENCE_REFRESHED_EVENT = "autorep:reference-refreshed";

export interface ReferenceRefreshedDetail {
  changed: boolean;
}
