import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { getErrorDetail } from "../../shared/api/apiError";
import { roleLabel } from "../../shared/constants/roles";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, type IconName, Input, PageHeader, Skeleton, Textarea, useToast,
} from "../../shared/ui";
import { useMyProfileQuery, useUpdateMyProfileMutation, useUserProfileQuery } from "./baseApi";
import { AvatarImage, ProfileAvatarUploader } from "./ProfileAvatar";
import type { UpdateProfileInput } from "./types";

/**
 * Self-service profile page. Modelled like an MS / Office account: the "Organization" card is
 * READ-ONLY (name, code, email, roles, designation, team, call centre, agency — all managed by
 * admins/HR, shown with a lock affordance), while the "Personal details" card lets the user edit
 * their own phone / location / bio and change their photo. Route /profile is the signed-in user;
 * /profile/:userId renders another (same-agency) user fully read-only.
 */
export function ProfilePage() {
  const { userId } = useParams();
  const isSelf = !userId;

  const myProfileQ = useMyProfileQuery(undefined, { skip: !isSelf });
  const otherProfileQ = useUserProfileQuery(userId ?? "", { skip: isSelf });
  const profile = isSelf ? myProfileQ.data : otherProfileQ.data;
  const isLoading = isSelf ? myProfileQ.isLoading : otherProfileQ.isLoading;
  const error = isSelf ? myProfileQ.error : otherProfileQ.error;

  const [update, { isLoading: saving }] = useUpdateMyProfileMutation();
  const toast = useToast();

  const [form, setForm] = useState<UpdateProfileInput>({ phoneNumber: "", location: "", bio: "" });
  useEffect(() => {
    if (!profile) return;
    setForm({ phoneNumber: profile.phoneNumber ?? "", location: profile.location ?? "", bio: profile.bio ?? "" });
  }, [profile]);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    try {
      await update(form).unwrap();
      toast.success("Profile saved", "Your personal details have been updated.");
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Try again.");
    }
  }

  if (isLoading) {
    return (
      <>
        <PageHeader eyebrow="Account" title="My Profile" />
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
          <Card><CardBody className="space-y-3 pt-6"><Skeleton className="h-28 w-28 rounded-full mx-auto" /><Skeleton className="h-4 w-32 mx-auto" /></CardBody></Card>
          <div className="lg:col-span-2 space-y-5">
            <Card><CardBody>{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-10 mb-3" />)}</CardBody></Card>
          </div>
        </div>
      </>
    );
  }

  if (!profile) {
    const forbidden = (error as { status?: number } | undefined)?.status === 403;
    return (
      <>
        <PageHeader eyebrow="Account" title={isSelf ? "My Profile" : "Profile"} />
        <Card><CardBody>
          <EmptyState
            icon={<Icon name={forbidden ? "lock" : "user"} size={20} />}
            title={forbidden ? "Not available" : "Profile not found"}
            description={forbidden
              ? "You can only view profiles of people in your own organization."
              : "This profile couldn't be loaded."}
          />
        </CardBody></Card>
      </>
    );
  }

  const displayName = profile.displayName || profile.userName || "User";
  const set = (k: keyof UpdateProfileInput) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value }));

  return (
    <>
      <PageHeader
        eyebrow={isSelf ? "Account" : "Team member"}
        title={isSelf ? "My Profile" : displayName}
        description={isSelf
          ? "Manage your photo and personal details. Your organization information is managed by your admins."
          : "Viewing a colleague's profile — read only."}
      />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 items-start">
        {/* Identity / avatar */}
        <Card>
          <CardBody className="flex flex-col items-center text-center gap-3 pt-6">
            {isSelf
              ? <ProfileAvatarUploader userId={profile.id} hasAvatar={profile.hasAvatar} name={displayName} />
              : <AvatarImage userId={profile.id} hasAvatar={profile.hasAvatar} name={displayName} size={112} className="ring-4" />}
            <div className="min-w-0">
              <div className="text-lg font-semibold text-ink-900 truncate">{displayName}</div>
              <div className="text-sm text-ink-500 truncate">{profile.userName}</div>
            </div>
            {profile.roles.length > 0 && (
              <div className="flex flex-wrap justify-center gap-1">
                {profile.roles.map((r) => <Badge key={r} tone="brand" variant="soft">{roleLabel(r)}</Badge>)}
              </div>
            )}
            {profile.location && (
              <div className="text-sm text-ink-600 flex items-center gap-1.5">
                <Icon name="mapPin" size={14} className="text-ink-400" />{profile.location}
              </div>
            )}
          </CardBody>
        </Card>

        {/* Organization (read-only) + Personal details */}
        <div className="lg:col-span-2 space-y-5">
          <Card>
            <CardBody>
              <SectionTitle icon="building" title="Organization" note="Managed by your organization" />
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4">
                <LockedField label="Name" value={profile.displayName} />
                <LockedField label="Code" value={profile.userName} />
                <LockedField label="Email" value={profile.email} />
                <LockedField label="Designation" value={profile.designation} />
                <LockedField label="Team" value={profile.teamName} />
                <LockedField label="Call centre" value={profile.callCenterName} />
                <LockedField label="Agency" value={profile.agencyName} />
                <LockedField label="Roles" value={profile.roles.map(roleLabel).join(", ")} />
              </div>
            </CardBody>
          </Card>

          <Card>
            <CardBody>
              <SectionTitle icon="user" title="Personal details" />
              {isSelf ? (
                <form onSubmit={save} className="space-y-4">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <Input label="Phone" value={form.phoneNumber ?? ""} onChange={set("phoneNumber")}
                      placeholder="e.g. +1 555 012 3456" maxLength={40} />
                    <Input label="Location" value={form.location ?? ""} onChange={set("location")}
                      placeholder="City, Country" maxLength={120} leftIcon={<Icon name="mapPin" size={15} />} />
                  </div>
                  <Textarea label="About" value={form.bio ?? ""} onChange={set("bio")}
                    placeholder="A short bio your teammates will see…" maxLength={1000} rows={4} />
                  <div className="flex justify-end">
                    <Button type="submit" loading={saving} leftIcon={<Icon name="save" size={15} />}>Save changes</Button>
                  </div>
                </form>
              ) : (
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4">
                  <ReadField label="Phone" value={profile.phoneNumber} />
                  <ReadField label="Location" value={profile.location} />
                  <ReadField label="About" value={profile.bio} full />
                </div>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
    </>
  );
}

function SectionTitle({ icon, title, note }: { icon: IconName; title: string; note?: string }) {
  return (
    <div className="flex items-center justify-between gap-2 mb-4 flex-wrap">
      <div className="flex items-center gap-2">
        <Icon name={icon} size={16} className="text-brand-500" />
        <h3 className="text-sm font-semibold text-ink-900">{title}</h3>
      </div>
      {note && (
        <span className="text-[11px] text-ink-400 flex items-center gap-1">
          <Icon name="lock" size={11} /> {note}
        </span>
      )}
    </div>
  );
}

/** A read-only org-owned field with a subtle lock affordance — the user cannot edit these. */
function LockedField({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="min-w-0">
      <div className="flex items-center gap-1 mb-1">
        <span className="text-[12px] font-medium text-ink-700">{label}</span>
        <Icon name="lock" size={11} className="text-ink-400" />
      </div>
      <div className="px-3 py-2 rounded-lg bg-ink-50 border hairline text-sm text-ink-800 truncate" title={value ?? undefined}>
        {value || <span className="text-ink-400">—</span>}
      </div>
    </div>
  );
}

function ReadField({ label, value, full }: { label: string; value?: string | null; full?: boolean }) {
  return (
    <div className={full ? "sm:col-span-2" : ""}>
      <div className="text-[12px] font-medium text-ink-700 mb-1">{label}</div>
      <div className="text-sm text-ink-800 whitespace-pre-wrap break-words">
        {value || <span className="text-ink-400">—</span>}
      </div>
    </div>
  );
}
