import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import {
  useListFeedQuery, useCreatePostMutation, useDeletePostMutation, useReactPostMutation,
  useCommentPostMutation, useDeleteFeedCommentMutation, useUploadFeedImageMutation, useUserDirectoryQuery,
} from "../../shared/api/baseApi";
import type { FeedPost, FeedComment, FeedPostKind } from "../../shared/api/types";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Avatar, Button, Card, CardBody, EmptyState, Icon, PageHeader, Skeleton, cn, useToast,
} from "../../shared/ui";
import { MentionBox, type MentionUser } from "./MentionBox";

const QUICK_EMOJIS = ["👍", "❤️", "🎉", "🔥", "👏", "😄", "🙌", "💯"];

const KINDS: { key: FeedPostKind; label: string; emoji: string }[] = [
  { key: "Update", label: "Update", emoji: "💬" },
  { key: "Announcement", label: "Announcement", emoji: "📣" },
  { key: "Praise", label: "Praise", emoji: "🎉" },
  { key: "Question", label: "Question", emoji: "❓" },
];
const KIND_META: Record<string, { emoji: string; label: string; accent: string; chip: string }> = {
  Update: { emoji: "", label: "", accent: "", chip: "" },
  Announcement: { emoji: "📣", label: "Announcement", accent: "border-l-4 border-l-amber-400", chip: "bg-amber-50 text-amber-700 border-amber-200" },
  Praise: { emoji: "🎉", label: "Praise", accent: "border-l-4 border-l-emerald-400", chip: "bg-emerald-50 text-emerald-700 border-emerald-200" },
  Question: { emoji: "❓", label: "Question", accent: "border-l-4 border-l-sky-400", chip: "bg-sky-50 text-sky-700 border-sky-200" },
};

function timeAgo(iso: string): string {
  const s = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return "just now";
  const m = Math.floor(s / 60); if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60); if (h < 24) return `${h}h ago`;
  const d = Math.floor(h / 24); if (d < 7) return `${d}d ago`;
  return new Date(iso).toLocaleDateString();
}

/** Render body text with @mentions highlighted (plain text — never dangerouslySetInnerHTML). */
function withMentions(body: string): ReactNode[] {
  return body.split(/(@[A-Za-z0-9._-]{2,32})/g).map((part, i) =>
    part.startsWith("@")
      ? <span key={i} className="text-brand-600 font-medium">{part}</span>
      : <span key={i}>{part}</span>);
}

/** An image that requires the bearer token — fetched as a blob (same pattern as ProfileAvatar). */
function AuthImage({ postId }: { postId: string }) {
  const token = useSelector((s: RootState) => s.auth.accessToken) ?? "";
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/feed/${postId}/image`, { headers: { Authorization: `Bearer ${token}` } });
        if (!res.ok || cancelled) return;
        const blob = await res.blob();
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      } catch { /* ignore — image just won't show */ }
    })();
    return () => { cancelled = true; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [postId, token]);
  if (!url) return <div className="mt-3 h-56 rounded-xl bg-ink-100 animate-pulse" />;
  return <img src={url} alt="Attached" loading="lazy" className="mt-3 rounded-xl max-h-[30rem] w-auto max-w-full border hairline" />;
}

/**
 * Pulse — the team social feed. Post updates (with a kind + optional image), react with emoji,
 * comment, and @mention teammates. Every post notifies the whole team. Polls so it stays live.
 */
export function PulsePage() {
  const me = useSelector((s: RootState) => s.auth.user);
  const { data: users } = useUserDirectoryQuery();
  const { data: posts, isLoading } = useListFeedQuery({ take: 30 }, { pollingInterval: 20_000 });
  const [createPost, { isLoading: posting }] = useCreatePostMutation();
  const [uploadImage, { isLoading: uploading }] = useUploadFeedImageMutation();
  const toast = useToast();

  const [draft, setDraft] = useState("");
  const [kind, setKind] = useState<FeedPostKind>("Update");
  const [imageKey, setImageKey] = useState<string | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  function resetComposer() {
    setDraft(""); setKind("Update"); setImageKey(null);
    if (imagePreview) URL.revokeObjectURL(imagePreview);
    setImagePreview(null);
    if (fileRef.current) fileRef.current.value = "";
  }

  async function pickImage(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!file.type.startsWith("image/") || file.size > 8 * 1024 * 1024) {
      toast.error("Image too large", "Please choose an image under 8 MB.");
      return;
    }
    try {
      const { key } = await uploadImage(file).unwrap();
      setImageKey(key);
      if (imagePreview) URL.revokeObjectURL(imagePreview);
      setImagePreview(URL.createObjectURL(file));
    } catch (err: unknown) {
      toast.error("Couldn't attach image", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function submit() {
    const body = draft.trim();
    if (!body && !imageKey) return;
    try {
      await createPost({ body, kind, imageKey }).unwrap();
      resetComposer();
    } catch (err: unknown) {
      toast.error("Couldn't post", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader eyebrow="Team" title="Pulse"
        description="Share an update, make an announcement, celebrate a win. React, comment, @mention — the whole team gets notified." />

      {/* Composer */}
      <Card className="mb-5">
        <CardBody className="flex gap-3">
          <Avatar name={me?.userName ?? "You"} size={40} />
          <div className="flex-1 min-w-0">
            {/* Kind selector */}
            <div className="flex items-center gap-1.5 flex-wrap mb-2">
              {KINDS.map((k) => (
                <button key={k.key} type="button" onClick={() => setKind(k.key)}
                  className={cn("inline-flex items-center gap-1 h-7 px-2.5 rounded-full text-xs border transition-colors",
                    kind === k.key ? "border-brand-300 bg-brand-50 text-brand-700 font-medium" : "border-ink-200 text-ink-600 hover:bg-ink-50")}>
                  <span>{k.emoji}</span>{k.label}
                </button>
              ))}
            </div>
            <MentionBox multiline value={draft} onChange={setDraft} onSubmit={submit} users={users ?? []}
              rows={draft ? 3 : 2}
              placeholder={kind === "Announcement" ? "Write an announcement for the team…" : "Share something with the team…  @name to mention."} />
            {imagePreview && (
              <div className="relative mt-2 inline-block">
                <img src={imagePreview} alt="Preview" className="max-h-40 rounded-lg border hairline" />
                <button type="button" onClick={() => { setImageKey(null); if (imagePreview) URL.revokeObjectURL(imagePreview); setImagePreview(null); if (fileRef.current) fileRef.current.value = ""; }}
                  aria-label="Remove image" title="Remove image"
                  className="absolute -top-2 -right-2 h-6 w-6 grid place-items-center rounded-full bg-ink-900 text-white shadow">
                  <Icon name="x" size={12} />
                </button>
              </div>
            )}
            <div className="flex items-center justify-between mt-2">
              <div className="flex items-center gap-2">
                <input ref={fileRef} type="file" accept="image/*" className="hidden" onChange={pickImage} />
                <Button size="sm" variant="ghost" loading={uploading} onClick={() => fileRef.current?.click()}
                  leftIcon={<Icon name="image" size={15} />}>Image</Button>
                <span className="text-xs text-ink-400 hidden sm:inline">⌘/Ctrl + Enter to post</span>
              </div>
              <Button size="sm" loading={posting} disabled={!draft.trim() && !imageKey} onClick={submit}
                leftIcon={<Icon name="send" size={14} />}>Post</Button>
            </div>
          </div>
        </CardBody>
      </Card>

      {/* Feed */}
      {isLoading ? (
        <div className="space-y-4">{[0, 1, 2].map((i) => <Card key={i}><CardBody><Skeleton className="h-20" /></CardBody></Card>)}</div>
      ) : !posts || posts.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="chat" size={20} />} title="Nothing here yet"
            description="Be the first to post — share a win, a shout-out, or an announcement for the team." />
        </CardBody></Card>
      ) : (
        <div className="space-y-4">
          {posts.map((p) => <PostCard key={p.id} post={p} meName={me?.userName ?? ""} users={users ?? []} />)}
        </div>
      )}
    </>
  );
}

function PostCard({ post, meName, users }: { post: FeedPost; meName: string; users: MentionUser[] }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [react] = useReactPostMutation();
  const [deletePost] = useDeletePostMutation();
  const [showPicker, setShowPicker] = useState(false);
  const [showComment, setShowComment] = useState(false);
  const meta = KIND_META[post.kind] ?? KIND_META.Update;

  async function toggle(emoji: string) {
    setShowPicker(false);
    try { await react({ postId: post.id, emoji }).unwrap(); }
    catch (err: unknown) { toast.error("Couldn't react", getErrorDetail(err) ?? "Try again."); }
  }

  async function remove() {
    if (!(await confirm({ title: "Delete post?", description: "This removes the post and its comments.", confirmLabel: "Delete", danger: true }))) return;
    try { await deletePost(post.id).unwrap(); toast.success("Post deleted"); }
    catch (err: unknown) { toast.error("Couldn't delete", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <Card className={cn("hover:shadow-card-hover transition-shadow", meta.accent)}>
      <CardBody>
        <div className="flex items-start gap-3">
          <Avatar name={post.authorName} size={40} />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-semibold text-ink-900">{post.authorName}</span>
              {meta.label && (
                <span className={cn("inline-flex items-center gap-1 text-[11px] px-1.5 py-0.5 rounded-full border", meta.chip)}>
                  {meta.emoji} {meta.label}
                </span>
              )}
              <span className="text-xs text-ink-400 tabular-nums">· {timeAgo(post.createdAt)}</span>
              {post.canDelete && (
                <button onClick={remove} aria-label="Delete post" title="Delete post"
                  className="ml-auto text-ink-300 hover:text-rose-500 transition-colors">
                  <Icon name="trash" size={14} />
                </button>
              )}
            </div>
            {post.body && <p className="text-sm text-ink-800 mt-1 whitespace-pre-wrap break-words leading-relaxed">{withMentions(post.body)}</p>}
            {post.hasImage && <AuthImage postId={post.id} />}

            {/* Reactions */}
            <div className="flex items-center gap-1.5 flex-wrap mt-3">
              {post.reactions.map((r) => (
                <button key={r.emoji} onClick={() => toggle(r.emoji)}
                  title={r.mine ? "Remove your reaction" : "React"}
                  className={cn("inline-flex items-center gap-1 h-7 px-2 rounded-full border text-xs transition-colors",
                    r.mine ? "border-brand-300 bg-brand-50 text-brand-700" : "border-ink-200 hover:bg-ink-50 text-ink-600")}>
                  <span className="text-sm leading-none">{r.emoji}</span>
                  <span className="tabular-nums">{r.count}</span>
                </button>
              ))}
              <div className="relative">
                <button onClick={() => setShowPicker((v) => !v)} aria-label="Add reaction" title="Add reaction"
                  className="inline-flex items-center justify-center h-7 w-7 rounded-full border border-ink-200 text-ink-400 hover:bg-ink-50 hover:text-ink-600 transition-colors">
                  <Icon name="plus" size={13} />
                </button>
                {showPicker && (
                  <div className="absolute z-10 mt-1 left-0 flex gap-1 p-1.5 rounded-xl border border-ink-200 bg-white shadow-pop">
                    {QUICK_EMOJIS.map((e) => (
                      <button key={e} onClick={() => toggle(e)} className="h-8 w-8 grid place-items-center rounded-lg hover:bg-ink-100 text-lg leading-none">{e}</button>
                    ))}
                  </div>
                )}
              </div>
              <button onClick={() => setShowComment((v) => !v)}
                className="inline-flex items-center gap-1 h-7 px-2 rounded-full text-xs text-ink-500 hover:bg-ink-50 transition-colors ml-1">
                <Icon name="chat" size={13} /> {post.comments.length > 0 ? post.comments.length : "Comment"}
              </button>
            </div>

            {/* Comments */}
            {(showComment || post.comments.length > 0) && (
              <div className="mt-3 pt-3 border-t hairline space-y-3">
                {post.comments.map((c) => <CommentRow key={c.id} comment={c} />)}
                <CommentComposer postId={post.id} meName={meName} users={users} />
              </div>
            )}
          </div>
        </div>
      </CardBody>
    </Card>
  );
}

function CommentRow({ comment }: { comment: FeedComment }) {
  const toast = useToast();
  const [del] = useDeleteFeedCommentMutation();
  return (
    <div className="flex items-start gap-2.5 group">
      <Avatar name={comment.authorName} size={28} />
      <div className="flex-1 min-w-0 rounded-xl bg-ink-50 px-3 py-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium text-ink-900">{comment.authorName}</span>
          <span className="text-[11px] text-ink-400 tabular-nums">· {timeAgo(comment.createdAt)}</span>
          {comment.canDelete && (
            <button onClick={async () => { try { await del(comment.id).unwrap(); } catch (err: unknown) { toast.error("Couldn't delete", getErrorDetail(err) ?? "Try again."); } }}
              aria-label="Delete comment" title="Delete comment"
              className="ml-auto opacity-0 group-hover:opacity-100 text-ink-300 hover:text-rose-500 transition">
              <Icon name="trash" size={12} />
            </button>
          )}
        </div>
        <p className="text-sm text-ink-700 mt-0.5 whitespace-pre-wrap break-words">{withMentions(comment.body)}</p>
      </div>
    </div>
  );
}

function CommentComposer({ postId, meName, users }: { postId: string; meName: string; users: MentionUser[] }) {
  const [body, setBody] = useState("");
  const [comment, { isLoading }] = useCommentPostMutation();
  const toast = useToast();

  async function send() {
    const text = body.trim();
    if (!text) return;
    try { await comment({ postId, body: text }).unwrap(); setBody(""); }
    catch (err: unknown) { toast.error("Couldn't comment", getErrorDetail(err) ?? "Try again."); }
  }

  return (
    <div className="flex items-center gap-2.5">
      <Avatar name={meName || "You"} size={28} />
      <div className="flex-1">
        <MentionBox value={body} onChange={setBody} onSubmit={send} users={users} ariaLabel="Write a comment"
          placeholder="Write a comment…  @name to mention"
          inputClassName="w-full h-9 rounded-full border border-ink-200 px-3.5 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20" />
      </div>
      <Button size="sm" variant="ghost" loading={isLoading} disabled={!body.trim()} onClick={send}>Send</Button>
    </div>
  );
}
