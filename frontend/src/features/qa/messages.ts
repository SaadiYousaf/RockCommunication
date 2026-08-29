/**
 * Centralized user-facing copy for the QA feature (rubrics + QA browser).
 * Keep inline message strings out of the pages — reference these instead.
 */
export const QA_MSG = {
  /** What failed to load, for the shared ErrorState ("Couldn't load QA rubrics"). */
  rubricsResourceName: "QA rubrics",
  /** What failed to load, for the shared ErrorState ("Couldn't load QA reviews"). */
  reviewsResourceName: "QA reviews",

  // Rubric mutations
  rubricCreated: "Rubric created",
  createRubricFailed: "Couldn't create rubric",

  // Rubrics empty state
  noRubricsTitle: "No rubrics yet",
  noRubricsDesc: "Create a rubric to standardize how your team scores calls.",

  // CSV export
  exportReadyTitle: "Export ready",
  exportReadyDesc: (count: number) => `${count} rows downloaded.`,

  // Browser empty states
  noScorecardsTitle: "No scorecards",
  noScorecardsDesc: "Once reviewers score calls, agent rollups will appear here.",
  noReviewsTitle: "No reviews",
  noReviewsDesc: "Reviews submitted by your QA team will show up here.",
} as const;
