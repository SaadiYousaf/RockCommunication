/**
 * The product's identity, in one place.
 *
 * The name used to be typed out inline in ten files — the login hero, the sidebar, the splash
 * screen, the page-title suffix, three alt attributes. Renaming meant finding all of them, and
 * missing one leaves the old name showing on exactly the screen nobody looks at twice.
 */
export const BRAND = {
  /** Short name. Sidebar, headers, page-title suffix — anywhere space is tight. */
  name: "SMH Achievers",

  /** Full legal name. Login footer, email templates, certificates. */
  fullName: "SMH Achievers Life Group",

  /** The line under the name in the lockup. */
  tagline: "Life Group",

  /** Wordmark split, so the lockup can weight the two halves differently. */
  wordmarkLead: "SMH",
  wordmarkTail: "ACHIEVERS",

  description:
    "SMH Achievers Life Group — modern call center and policy management platform.",

  copyright: (year: number) => `© ${year} SMH Achievers Life Group. All rights reserved.`,
} as const;
