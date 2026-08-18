/**
 * Centralized user-facing copy for the Profile feature (profile page + avatar uploader).
 * Keep inline message strings out of the components — reference these instead.
 */
export const PROFILE_MSG = {
  // Save personal details
  profileSaved: "Profile saved",
  profileSavedDesc: "Your personal details have been updated.",
  saveFailed: "Couldn't save",

  // Access / not-found empty states
  notAvailableTitle: "Not available",
  notAvailableDesc: "You can only view profiles of people in your own organization.",
  notFoundTitle: "Profile not found",
  notFoundDesc: "This profile couldn't be loaded.",

  // Avatar upload
  photoUpdated: "Photo updated",
  chooseImageFile: "Please choose an image file.",
  imageTooLarge: "Image must be under 5 MB.",
  avatarUploadFailed: "Upload failed — use an image under 5 MB.",
} as const;
