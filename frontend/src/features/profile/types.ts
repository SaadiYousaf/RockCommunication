/**
 * Self-service profile types — modelled like an MS / Office account. The org-owned fields
 * (displayName, userName, email, roles, designation, teamName, callCenterName, agencyName) are
 * READ-ONLY to the user; only phoneNumber, location, bio and the avatar are personal + editable.
 */
export interface UserProfile {
  id: string;
  // ── Read-only, org-owned ──
  userName: string;
  displayName?: string | null;
  email: string;
  roles: string[];
  agencyId: string;
  agencyName?: string | null;
  callCenterId?: string | null;
  callCenterName?: string | null;
  teamId?: string | null;
  teamName?: string | null;
  designation?: string | null;
  // ── Editable, personal ──
  phoneNumber?: string | null;
  location?: string | null;
  bio?: string | null;
  hasAvatar: boolean;
}

/** The only fields a user may change on their own profile. */
export interface UpdateProfileInput {
  phoneNumber?: string | null;
  location?: string | null;
  bio?: string | null;
}
