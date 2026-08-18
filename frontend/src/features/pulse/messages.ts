/**
 * Centralized user-facing copy for the Pulse feature (the team social feed: posts, reactions,
 * comments, polls). Toast, confirm-dialog and empty-state strings live here so the same wording
 * isn't duplicated across the feed's many components.
 */
export const PULSE_MSG = {
  retry: "Try again.",

  imageTooLarge: "Image too large",
  imageTooLargeDesc: "Please choose an image under 8 MB.",
  attachImageFailed: "Couldn't attach image",
  postFailed: "Couldn't post",

  nothingHereTitle: "Nothing here yet",
  nothingHereDesc: "Be the first to post — share a win, a shout-out, or an announcement for the team.",

  reactFailed: "Couldn't react",
  deletePostTitle: "Delete post?",
  deletePostDesc: "This removes the post and its comments.",
  deleteLabel: "Delete",
  postDeleted: "Post deleted",
  deleteFailed: "Couldn't delete",

  voteFailed: "Couldn't record your vote",
  commentFailed: "Couldn't comment",
} as const;
