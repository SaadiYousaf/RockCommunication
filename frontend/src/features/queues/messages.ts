/**
 * Centralized user-facing copy for the Telephony (queues / voicemail / public endpoints) feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const QUEUES_MSG = {
  // CSV export
  exportReadyTitle: "Export ready",
  exportReadyDesc: (count: number) => `${count} rows downloaded.`,

  // Search (shared across the sections on this page)
  noMatchesDesc: "Try a different search.",

  // Queues
  queueCreated: "Queue created",
  queueCreatedDesc: (name: string) => `${name} is ready to receive calls.`,
  createQueueFailed: "Couldn't create queue",
  noQueuesTitle: "No queues yet",
  noQueuesDesc: "Create an inbound queue to start routing customer calls.",
  queueSearchPlaceholder: "Search queues by name, phone or skill…",
  noQueuesMatchTitle: "No queues match",

  // Voicemail assets
  voicemailSaved: "Voicemail saved",
  saveVoicemailFailed: "Couldn't save voicemail",
  noVoicemailsTitle: "No voicemail assets",
  noVoicemailsDesc: "Upload a recording URL so agents can drop messages on no-answer.",
  voicemailSearchPlaceholder: "Search voicemail assets by name…",
  noVoicemailsMatchTitle: "No voicemail assets match",

  // Public lead-capture endpoints
  endpointCreated: "Endpoint created",
  endpointCreatedDesc: (slug: string) => `Slug: ${slug}`,
  createEndpointFailed: "Couldn't create endpoint",
  copiedTitle: "Copied",
  copiedDesc: "Secret copied to your clipboard.",
  copyFailedTitle: "Couldn't copy",
  copyFailedDesc: "Your browser blocked clipboard access.",
  noEndpointsTitle: "No endpoints yet",
  noEndpointsDesc: "Generate one to capture leads from your website forms.",
  endpointSearchPlaceholder: "Search endpoints by slug…",
  noEndpointsMatchTitle: "No endpoints match",
} as const;
