import { baseApi } from "../../shared/api/baseApi";

/** One person's end-of-day answers for one working day. */
export interface ShiftReflection {
  id: string;
  userId: string;
  userName: string;
  shiftDate: string;
  learned: string | null;
  didJustice: boolean | null;
  justiceNote: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface SubmitReflectionInput {
  learned?: string | null;
  didJustice?: boolean | null;
  justiceNote?: string | null;
}

export interface ListReflectionsArgs {
  from?: string;
  to?: string;
  userId?: string;
  /** "mine" limits to the caller's own history; anything else is the whole scope they may read. */
  scope?: string;
}

/**
 * End-of-day reflections. The mutation is fired from the sign-out dialog; the query feeds the
 * review page.
 *
 * The mutation invalidates nothing on purpose — it runs microseconds before the session is torn
 * down, and an invalidation would kick off refetches with a token that is about to be discarded.
 */
const reflectionsApi = baseApi.injectEndpoints({
  endpoints: (b) => ({
    listReflections: b.query<ShiftReflection[], ListReflectionsArgs | void>({
      query: (args) => ({ url: "/api/hr/reflections", params: args ?? undefined }),
      providesTags: ["Reflections"],
    }),
    submitReflection: b.mutation<ShiftReflection, SubmitReflectionInput>({
      query: (body) => ({ url: "/api/hr/reflections", method: "POST", body }),
    }),
  }),
  overrideExisting: false,
});

export const { useListReflectionsQuery, useSubmitReflectionMutation } = reflectionsApi;
