import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import {
  useListFeedQuery, useCreatePostMutation, useDeletePostMutation, useReactPostMutation,
  useCommentPostMutation, useDeleteFeedCommentMutation, useUploadFeedImageMutation, useUserDirectoryQuery,
  useVotePollMutation,
} from "../../shared/api/baseApi";
import type { FeedPost, FeedComment, FeedPostKind, FeedPoll } from "../../shared/api/types";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Avatar, Button, Card, CardBody, EmptyState, Icon, PageHeader, Skeleton, cn, useToast,
} from "../../shared/ui";
import { MentionBox, type MentionUser } from "./MentionBox";
import { PULSE_MSG } from "./messages";

const QUICK_EMOJIS = ["👍", "❤️", "🎉", "🔥", "👏", "😄", "🙌", "💯"];

const KINDS: { key: FeedPostKind; label: string; emoji: string }[] = [
  { key: "Update", label: "Update", emoji: "💬" },
  { key: "Announcement", label: "Announcement", emoji: "📣" },
  { key: "Praise", label: "Praise", emoji: "🎉" },
  { key: "Question", label: "Question", emoji: "❓" },
  { key: "Poll", label: "Poll", emoji: "📊" },
];
const KIND_META: Record<string, { emoji: string; label: string; accent: string; chip: string }> = {
  Update: { emoji: "", label: "", accent: "", chip: "" },
  Announcement: { emoji: "📣", label: "Announcement", accent: "border-l-[3px] border-l-amber-400 bg-gradient-to-r from-amber-50/70 via-white to-white", chip: "bg-amber-50 text-amber-700 border-amber-200" },
  Praise: { emoji: "🎉", label: "Praise", accent: "border-l-[3px] border-l-emerald-400 bg-gradient-to-r from-emerald-50/60 via-white to-white", chip: "bg-emerald-50 text-emerald-700 border-emerald-200" },
  Question: { emoji: "❓", label: "Question", accent: "border-l-[3px] border-l-sky-400 bg-gradient-to-r from-sky-50/60 via-white to-white", chip: "bg-sky-50 text-sky-700 border-sky-200" },
  Poll: { emoji: "📊", label: "Poll", accent: "border-l-[3px] border-l-violet-400 bg-gradient-to-r from-violet-50/60 via-white to-white", chip: "bg-violet-50 text-violet-700 border-violet-200" },
};

const MIN_POLL_OPTIONS = 2;
const MAX_POLL_OPTIONS = 8;

function timeAgo(iso: string): string {
  const s = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return "just now";
  const m = Math.floor(s / 60); if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60); if (h < 24) return `${h}h ago`;
  const d = Math.floor(h / 24); if (d < 7) return `${d}d ago`;
  return new Date(iso).toLocaleDateString();
}

/**
 * Render body text with @mentions highlighted (plain text — never dangerouslySetInnerHTML).
 * When onMention is given, a mention is a button that opens a DM with that teammate.
 */
function withMentions(body: string, onMention?: (username: string) => void): ReactNode[] {
  return body.split(/(@[A-Za-z0-9._-]{2,32})/g).map((part, i) => {
    if (!part.startsWith("@")) return <span key={i}>{part}</span>;
    const name = part.slice(1);
    return onMention
      ? <button key={i} type="button" onClick={() => onMention(name)} title={`Message ${name}`}
          className="text-brand-600 font-medium hover:underline">{part}</button>
      : <span key={i} className="text-brand-600 font-medium">{part}</span>;
  });
}

/** An image that requires the bearer token — fetched as a blob (same pattern as ProfileAvatar). */
function AuthImage({ postId }: { postId: string }) {
  const token = useSelector((s: RootState) => s.auth.accessToken) ?? "";
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/feed/${postId}/image`, { headers: { Authorization: `Bearer ${token}` } });
        if (cancelled) return;
        if (!res.ok) { setFailed(true); return; }
        const blob = await res.blob();
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      } catch { if (!cancelled) setFailed(true); }
    })();
    return () => { cancelled = true; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [postId, token]);
  // Never leave a permanent grey box: hide entirely if the image is gone/unauthorised.
  if (failed) return null;
  if (!url) return <div className="mt-3 h-40 rounded-2xl bg-ink-100 animate-pulse" />;
  return (
    <img src={url} alt="Attached" loading="lazy" onError={() => setFailed(true)}
      className="mt-3 rounded-2xl max-h-[30rem] w-auto max-w-full border hairline shadow-card object-cover" />
  );
}

/**
 * Pulse — the team social feed. Post updates (with a kind + optional image), react with emoji,
 * comment, and @mention teammates. Every post notifies the whole team. Polls so it stays live.
 */
export function PulsePage() {
  const me = useSelector((s: RootState) => s.auth.user);
  const navigate = useNavigate();
  const { data: users } = useUserDirectoryQuery();
  const { data: posts, isLoading } = useListFeedQuery({ take: 30 }, { pollingInterval: 20_000 });

  // Clicking a @mention or an author opens a direct message with that person.
  const usersByName = useMemo(() => new Map((users ?? []).map((u) => [u.userName.toLowerCase(), u.id])), [users]);
  const openDmById = (userId: string) => { if (userId && userId !== me?.id) navigate(`/chat?dm=${userId}`); };
  const openDmByName = (username: string) => { const id = usersByName.get(username.toLowerCase()); if (id) openDmById(id); };
  const [createPost, { isLoading: posting }] = useCreatePostMutation();
  const [uploadImage, { isLoading: uploading }] = useUploadFeedImageMutation();
  const toast = useToast();

  const [draft, setDraft] = useState("");
  const [kind, setKind] = useState<FeedPostKind>("Update");
  const [pollOptions, setPollOptions] = useState<string[]>(["", ""]);
  const [imageKey, setImageKey] = useState<string | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  // Revoke a staged preview object URL on unmount/replace (the manual revokes cover the other paths).
  useEffect(() => () => { if (imagePreview) URL.revokeObjectURL(imagePreview); }, [imagePreview]);

  const isPoll = kind === "Poll";
  const validPollOptions = pollOptions.map((o) => o.trim()).filter(Boolean);
  const setOption = (i: number, v: string) => setPollOptions((o) => o.map((x, j) => (j === i ? v : x)));
  const addOption = () => setPollOptions((o) => (o.length >= MAX_POLL_OPTIONS ? o : [...o, ""]));
  const removeOption = (i: number) => setPollOptions((o) => (o.length <= MIN_POLL_OPTIONS ? o : o.filter((_, j) => j !== i)));

  function resetComposer() {
    setDraft(""); setKind("Update"); setImageKey(null); setPollOptions(["", ""]);
    if (imagePreview) URL.revokeObjectURL(imagePreview);
    setImagePreview(null);
    if (fileRef.current) fileRef.current.value = "";
  }

  async function pickImage(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!file.type.startsWith("image/") || file.size > 8 * 1024 * 1024) {
      toast.error(PULSE_MSG.imageTooLarge, PULSE_MSG.imageTooLargeDesc);
      return;
    }
    try {
      const { key } = await uploadImage(file).unwrap();
      setImageKey(key);
      if (imagePreview) URL.revokeObjectURL(imagePreview);
      setImagePreview(URL.createObjectURL(file));
    } catch (err: unknown) {
      toast.error(PULSE_MSG.attachImageFailed, getErrorDetail(err) ?? PULSE_MSG.retry);
    }
  }

  // The composer can post when there's text/image, or (for a poll) a question + ≥2 options.
  const canPost = isPoll
    ? draft.trim().length > 0 && validPollOptions.length >= MIN_POLL_OPTIONS
    : draft.trim().length > 0 || !!imageKey;

  async function submit() {
    const body = draft.trim();
    if (!canPost) return;
    try {
      await createPost(
        isPoll
          ? { body, kind, options: validPollOptions }
          : { body, kind, imageKey },
      ).unwrap();
      resetComposer();
    } catch (err: unknown) {
      toast.error(PULSE_MSG.postFailed, getErrorDetail(err) ?? PULSE_MSG.retry);
    }
  }

  return (
    <>
      <PageHeader eyebrow="Team" title="Pulse"
        badge={<LivePulseBadge />}
        description="Share an update, make an announcement, run a poll, celebrate a win. React, comment, @mention — the whole team gets notified." />

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
              placeholder={isPoll ? "Ask the team a question…" : kind === "Announcement" ? "Write an announcement for the team…" : "Share something with the team…  @name to mention."} />

            {isPoll && (
              <div className="mt-2.5 space-y-2 animate-fade-up">
                {pollOptions.map((opt, i) => (
                  <div key={i} className="flex items-center gap-2">
                    <span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-violet-50 text-[11px] font-semibold text-violet-600">{i + 1}</span>
                    <input value={opt} onChange={(e) => setOption(i, e.target.value)} maxLength={200}
                      placeholder={`Option ${i + 1}`}
                      className="flex-1 h-9 rounded-lg border border-ink-200 px-3 text-sm outline-none focus:border-violet-400 focus:ring-2 focus:ring-violet-500/20" />
                    {pollOptions.length > MIN_POLL_OPTIONS && (
                      <button type="button" onClick={() => removeOption(i)} aria-label="Remove option" title="Remove option"
                        className="text-ink-300 hover:text-rose-500 transition-colors"><Icon name="x" size={14} /></button>
                    )}
                  </div>
                ))}
                {pollOptions.length < MAX_POLL_OPTIONS && (
                  <button type="button" onClick={addOption}
                    className="inline-flex items-center gap-1 text-xs font-medium text-violet-600 hover:text-violet-700 transition-colors">
                    <Icon name="plus" size={13} /> Add option
                  </button>
                )}
              </div>
            )}
            {imagePreview && !isPoll && (
              <div className="relative mt-2 inline-block">
                <img src={imagePreview} alt="Preview" className="max-h-40 rounded-lg border hairline" />
                <button type="button" onClick={() => { setImageKey(null); if (imagePreview) URL.revokeObjectURL(imagePreview); setImagePreview(null); if (fileRef.current) fileRef.current.value = ""; }}
                  aria-label="Remove image" title="Remove image"
                  className="absolute -top-2 -right-2 h-6 w-6 grid place-items-center rounded-full bg-ink-900 text-white shadow">
                  <Icon name="x" size={12} />
                </button>
              </div>
            )}
            <div className="flex items-center justify-between mt-2.5 pt-2.5 border-t hairline">
              <div className="flex items-center gap-2">
                <input ref={fileRef} type="file" accept="image/*" className="hidden" onChange={pickImage} />
                {!isPoll && (
                  <Button size="sm" variant="ghost" loading={uploading} onClick={() => fileRef.current?.click()}
                    leftIcon={<Icon name="image" size={15} />}>Image</Button>
                )}
                {isPoll
                  ? <span className="text-xs text-ink-400">Add {MIN_POLL_OPTIONS}–{MAX_POLL_OPTIONS} options · everyone can vote</span>
                  : <span className="text-xs text-ink-400 hidden sm:inline">⌘/Ctrl + Enter to post</span>}
              </div>
              <Button size="sm" loading={posting} disabled={!canPost} onClick={submit}
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
          <EmptyState icon={<Icon name="chat" size={20} />} title={PULSE_MSG.nothingHereTitle}
            description={PULSE_MSG.nothingHereDesc} />
        </CardBody></Card>
      ) : (
        <div className="space-y-4">
          {posts.map((p, i) => (
            <div key={p.id} className="animate-rise" style={{ animationDelay: `${Math.min(i, 6) * 45}ms` }}>
              <PostCard post={p} meName={me?.userName ?? ""} users={users ?? []}
                onMention={openDmByName} onOpenDm={openDmById} />
            </div>
          ))}
        </div>
      )}
    </>
  );
}

/** A quietly-pulsing "Live" chip — the feed auto-refreshes, so the badge shows it's alive. */
function LivePulseBadge() {
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-brand-200 bg-brand-50/70 px-2.5 py-1 text-[11px] font-medium text-brand-700">
      <span className="relative flex h-2 w-2">
        <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-400 opacity-75" />
        <span className="relative inline-flex h-2 w-2 rounded-full bg-brand-500" />
      </span>
      Live
    </span>
  );
}

function PostCard({ post, meName, users, onMention, onOpenDm }: {
  post: FeedPost; meName: string; users: MentionUser[];
  onMention: (username: string) => void; onOpenDm: (userId: string) => void;
}) {
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
    catch (err: unknown) { toast.error(PULSE_MSG.reactFailed, getErrorDetail(err) ?? PULSE_MSG.retry); }
  }

  async function remove() {
    if (!(await confirm({ title: PULSE_MSG.deletePostTitle, description: PULSE_MSG.deletePostDesc, confirmLabel: PULSE_MSG.deleteLabel, danger: true }))) return;
    try { await deletePost(post.id).unwrap(); toast.success(PULSE_MSG.postDeleted); }
    catch (err: unknown) { toast.error(PULSE_MSG.deleteFailed, getErrorDetail(err) ?? PULSE_MSG.retry); }
  }

  return (
    <Card className={cn("hover:shadow-card-hover transition-shadow", meta.accent)}>
      <CardBody>
        <div className="flex items-start gap-3">
          <button type="button" onClick={() => onOpenDm(post.authorUserId)} title={`Message ${post.authorName}`}
            className="shrink-0 rounded-full ring-2 ring-white shadow-sm hover:ring-brand-100 transition">
            <Avatar name={post.authorName} size={40} />
          </button>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <button type="button" onClick={() => onOpenDm(post.authorUserId)} title={`Message ${post.authorName}`}
                className="font-semibold text-ink-900 hover:text-brand-600 transition-colors">{post.authorName}</button>
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
            {post.body && <p className="text-sm text-ink-800 mt-1 whitespace-pre-wrap break-words leading-relaxed">{withMentions(post.body, onMention)}</p>}
            {post.hasImage && <AuthImage postId={post.id} />}
            {post.poll && <PollBlock postId={post.id} poll={post.poll} />}

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
                {post.comments.map((c) => <CommentRow key={c.id} comment={c} onMention={onMention} onOpenDm={onOpenDm} />)}
                <CommentComposer postId={post.id} meName={meName} users={users} />
              </div>
            )}
          </div>
        </div>
      </CardBody>
    </Card>
  );
}

/**
 * A live poll: tap an option to vote (tap your choice again to un-vote, tap another to move it).
 * Once you've voted, every option reveals its share as an animated bar. Results are always visible
 * to the author-agnostic team — this mirrors MS Teams / Slack polls.
 */
function PollBlock({ postId, poll }: { postId: string; poll: FeedPoll }) {
  const [vote, { isLoading }] = useVotePollMutation();
  const toast = useToast();
  const voted = poll.myOptionId != null;
  const total = poll.totalVotes;

  async function cast(optionId: string) {
    try { await vote({ postId, optionId }).unwrap(); }
    catch (err: unknown) { toast.error(PULSE_MSG.voteFailed, getErrorDetail(err) ?? PULSE_MSG.retry); }
  }

  return (
    <div className="mt-3 space-y-2">
      {poll.options.map((o) => {
        const pct = total > 0 ? Math.round((o.votes / total) * 100) : 0;
        const mine = poll.myOptionId === o.id;
        return (
          <button key={o.id} type="button" disabled={isLoading} onClick={() => cast(o.id)}
            title={mine ? "Tap to remove your vote" : "Tap to vote"}
            className={cn(
              "relative w-full overflow-hidden rounded-xl border px-3 py-2 text-left text-sm transition-colors disabled:opacity-70",
              mine ? "border-violet-300 bg-violet-50/40" : "border-ink-200 hover:border-violet-300 hover:bg-violet-50/30")}>
            {/* Result bar — only after the viewer has voted, so it doesn't bias the vote. */}
            {voted && (
              <span aria-hidden style={{ width: `${pct}%` }}
                className={cn("absolute inset-y-0 left-0 rounded-xl transition-[width] duration-500 ease-out-quint",
                  mine ? "bg-violet-200/60" : "bg-ink-100")} />
            )}
            <span className="relative flex items-center justify-between gap-2">
              <span className="flex items-center gap-1.5 min-w-0">
                {mine && <Icon name="check" size={13} className="shrink-0 text-violet-600" />}
                <span className={cn("truncate", mine ? "font-medium text-violet-800" : "text-ink-800")}>{o.text}</span>
              </span>
              {voted && <span className="shrink-0 tabular-nums text-xs text-ink-500">{pct}%</span>}
            </span>
          </button>
        );
      })}
      <p className="text-xs text-ink-400">
        {total === 0 ? "No votes yet — be the first." : `${total} ${total === 1 ? "vote" : "votes"}`}
        {voted && " · tap your choice again to remove it"}
      </p>
    </div>
  );
}

function CommentRow({ comment, onMention, onOpenDm }: {
  comment: FeedComment; onMention: (username: string) => void; onOpenDm: (userId: string) => void;
}) {
  const toast = useToast();
  const [del] = useDeleteFeedCommentMutation();
  return (
    <div className="flex items-start gap-2.5 group">
      <button type="button" onClick={() => onOpenDm(comment.authorUserId)} title={`Message ${comment.authorName}`} className="shrink-0">
        <Avatar name={comment.authorName} size={28} />
      </button>
      <div className="flex-1 min-w-0 rounded-xl bg-ink-50 px-3 py-2">
        <div className="flex items-center gap-2">
          <button type="button" onClick={() => onOpenDm(comment.authorUserId)} title={`Message ${comment.authorName}`}
            className="text-sm font-medium text-ink-900 hover:text-brand-600 transition-colors">{comment.authorName}</button>
          <span className="text-[11px] text-ink-400 tabular-nums">· {timeAgo(comment.createdAt)}</span>
          {comment.canDelete && (
            <button onClick={async () => { try { await del(comment.id).unwrap(); } catch (err: unknown) { toast.error(PULSE_MSG.deleteFailed, getErrorDetail(err) ?? PULSE_MSG.retry); } }}
              aria-label="Delete comment" title="Delete comment"
              className="ml-auto opacity-0 group-hover:opacity-100 text-ink-300 hover:text-rose-500 transition">
              <Icon name="trash" size={12} />
            </button>
          )}
        </div>
        <p className="text-sm text-ink-700 mt-0.5 whitespace-pre-wrap break-words">{withMentions(comment.body, onMention)}</p>
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
    catch (err: unknown) { toast.error(PULSE_MSG.commentFailed, getErrorDetail(err) ?? PULSE_MSG.retry); }
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
