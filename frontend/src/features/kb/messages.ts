/**
 * Centralized user-facing copy for the Knowledge Base feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const KB_MSG = {
  // Search placeholders (no user-facing copy inline).
  searchPlaceholder: "Search articles…",
  // Article mutations
  articleSaved: "Article saved",
  saveArticleFailed: "Couldn't save article",

  // List empty state
  noArticlesTitle: "No articles",
  noResultsDesc: (q: string) => `No results for "${q}"`,
  noArticlesDesc: "Get started by creating your first article.",

  // Reader empty state
  pickArticleTitle: "Pick an article to read",
  pickArticleDesc: "Search or browse the list to read knowledge base entries.",
} as const;
