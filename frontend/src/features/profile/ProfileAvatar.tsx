import { useEffect, useRef, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import { Avatar, Button, Icon, cn, useToast } from "../../shared/ui";
import { useUploadAvatarMutation } from "./baseApi";
import { PROFILE_MSG } from "./messages";

const MAX_AVATAR_BYTES = 5 * 1024 * 1024;

/**
 * Display-only avatar that streams the image through the authorized endpoint (bearer token → blob
 * → object URL), exactly like EmployeeImageField, and falls back to the initials Avatar when the
 * user has no photo. `version` lets a parent force a re-fetch after an upload even though
 * `hasAvatar` didn't change (the bytes did).
 */
export function AvatarImage({
  userId, hasAvatar, name, size = 40, version = 0, className,
}: {
  userId: string;
  hasAvatar: boolean;
  name: string;
  size?: number;
  version?: number;
  className?: string;
}) {
  const token = useSelector((s: RootState) => s.auth.accessToken) ?? "";
  const [preview, setPreview] = useState<string | null>(null);

  useEffect(() => {
    if (!hasAvatar) { setPreview(null); return; }
    let url: string | null = null;
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/profile/${userId}/avatar?v=${version}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!res.ok || cancelled) return;
        const blob = await res.blob();
        if (cancelled) return;
        url = URL.createObjectURL(blob);
        setPreview(url);
      } catch { /* preview is best-effort — fall back to initials */ }
    })();
    return () => { cancelled = true; if (url) URL.revokeObjectURL(url); };
  }, [userId, hasAvatar, token, version]);

  if (preview) {
    return (
      <img
        src={preview}
        alt={name}
        className={cn("rounded-full object-cover ring-2 ring-white shadow-sm", className)}
        style={{ width: size, height: size }}
      />
    );
  }
  return <Avatar name={name} size={size} className={className} />;
}

/**
 * The large profile avatar with an "upload / change photo" control. Self-only — rendered on the
 * signed-in user's own profile. Posts multipart to /api/profile/me/avatar (validated + capped at
 * 5 MB server-side too), then bumps `version` so the new photo loads immediately.
 */
export function ProfileAvatarUploader({
  userId, hasAvatar, name, onChanged,
}: {
  userId: string;
  hasAvatar: boolean;
  name: string;
  onChanged?: () => void;
}) {
  const [upload, { isLoading }] = useUploadAvatarMutation();
  const [version, setVersion] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const toast = useToast();

  async function onSelect(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!file.type.startsWith("image/")) { setError(PROFILE_MSG.chooseImageFile); return; }
    if (file.size > MAX_AVATAR_BYTES) { setError(PROFILE_MSG.imageTooLarge); return; }
    setError(null);
    try {
      const fd = new FormData();
      fd.append("file", file);
      await upload(fd).unwrap();
      setVersion((v) => v + 1);
      onChanged?.();
      toast.success(PROFILE_MSG.photoUpdated);
    } catch {
      setError(PROFILE_MSG.avatarUploadFailed);
    } finally {
      if (inputRef.current) inputRef.current.value = "";
    }
  }

  return (
    <div className="flex flex-col items-center gap-3">
      <div className="relative">
        <AvatarImage userId={userId} hasAvatar={hasAvatar} name={name} size={112} version={version} className="ring-4" />
        <button
          type="button"
          onClick={() => inputRef.current?.click()}
          disabled={isLoading}
          aria-label="Change photo"
          className="absolute -bottom-1 -right-1 h-9 w-9 rounded-full bg-brand-600 text-white grid place-items-center shadow ring-2 ring-white hover:bg-brand-700 disabled:opacity-60 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40"
        >
          <Icon name="upload" size={15} />
        </button>
      </div>
      <input ref={inputRef} type="file" accept="image/*" className="hidden" onChange={onSelect} />
      <Button variant="outline" size="sm" loading={isLoading} onClick={() => inputRef.current?.click()}
        leftIcon={<Icon name="upload" size={14} />}>
        {hasAvatar ? "Change photo" : "Upload photo"}
      </Button>
      {error && <div className="text-[11px] text-rose-600">{error}</div>}
    </div>
  );
}
