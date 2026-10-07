/**
 * Centralized user-facing copy for the Documents feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const DOCUMENTS_MSG = {
  sheets: "Sheets in this workbook",
  emptySheet: "This sheet is empty.",
  fullScreen: "Full screen",
  exitFullScreen: "Exit full screen (Esc)",
  fetchFailed: "Couldn't load this document. It may have been removed.",
  tooLargeToRender: "This file is too large to display in full. Ask whoever shared it for a smaller extract.",
  truncatedRows: (shown: number, hidden: number) =>
    `Showing the first ${shown.toLocaleString()} rows. ${hidden.toLocaleString()} more are in the file but aren't displayed here.`,

  /** What failed to load, for the shared ErrorState ("Couldn't load documents"). */
  resourceName: "documents",

  // Upload
  uploaded: "Uploaded",
  uploadFailed: "Upload failed",

  // Delete
  deleteConfirmTitle: (name: string) => `Delete "${name}"?`,
  deleteConfirmDesc: "This permanently removes the document for everyone in the agency.",
  deleted: "Deleted",
  deleteFailed: "Delete failed",

  // Notes
  saveNoteFailed: "Couldn't save note",

  // Library empty states
  noDocumentsTitle: "No documents yet",
  noDocumentsManageDesc: "Upload one above to get started.",
  noDocumentsReadonlyDesc: "Documents shared by your office will appear here.",
  selectDocumentTitle: "Select a document",
  selectDocumentDesc: "Pick a file from the library to read it in the protected viewer.",

  // Protected viewer
  renderFailed: "Couldn't render this document. The file may be an unsupported format.",
} as const;
