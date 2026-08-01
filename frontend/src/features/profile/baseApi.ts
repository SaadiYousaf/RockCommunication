import { baseApi } from "../../shared/api/baseApi";
import type { UserProfile, UpdateProfileInput } from "./types";

/**
 * Profile endpoints injected into the single shared RTK Query instance (same pattern the rest of
 * the app relies on — one api slice, one auth-refreshing baseQuery, cross-feature tag invalidation).
 * The avatar IMAGE itself is fetched as an authed blob (see ProfileAvatar), not through RTK, exactly
 * like EmployeeImageField — these endpoints only carry JSON + the multipart upload.
 */
const profileApi = baseApi.injectEndpoints({
  endpoints: (b) => ({
    myProfile: b.query<UserProfile, void>({
      query: () => ({ url: "/api/profile/me" }),
      providesTags: ["Profile"],
    }),
    userProfile: b.query<UserProfile, string>({
      query: (userId) => ({ url: `/api/profile/${userId}` }),
      providesTags: ["Profile"],
    }),
    updateMyProfile: b.mutation<UserProfile, UpdateProfileInput>({
      query: (body) => ({ url: "/api/profile/me", method: "PUT", body }),
      invalidatesTags: ["Profile"],
    }),
    uploadAvatar: b.mutation<{ key: string }, FormData>({
      query: (body) => ({ url: "/api/profile/me/avatar", method: "POST", body }),
      invalidatesTags: ["Profile"],
    }),
  }),
  overrideExisting: false,
});

export const {
  useMyProfileQuery,
  useUserProfileQuery,
  useUpdateMyProfileMutation,
  useUploadAvatarMutation,
} = profileApi;
