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

  // Manage access (admin editing another user's roles)
  manageAccessTitle: "Manage access",
  manageAccessNote: "Admin only",
  manageAccessBlurb: (name: string) =>
    `Roles control what ${name} can see and do — for example promoting them to Admin. Changes take effect on their next sign-in and they'll be notified.`,
  noRolesAssigned: "No roles assigned",
  editRoles: "Edit roles",
  editRolesTitle: (name: string) => `Edit roles — ${name}`,
  accessUpdated: "Access updated",
  accessUpdatedDesc: (name: string) => `${name}'s roles have been updated.`,
  accessUpdateFailed: "Couldn't update access",
} as const;
