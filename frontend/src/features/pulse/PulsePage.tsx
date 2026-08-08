import { getErrorDetail } from "../../shared/api/apiError";
import { useState, type ReactNode } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import {
  useListFeedQuery, useCreatePostMutation, useDeletePostMutation, useReactPostMutation,
  useCommentPostMutation, useDeleteFeedCommentMutation,
} from "../../shared/api/baseApi";
import type { FeedPost, FeedComment } from "../../shared/api/types";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Avatar, Button, Card, CardBody, EmptyState, Icon, PageHeader, Skeleton, Textarea, useToast,
} from "../../shared/ui";

const QUICK_EMOJIS = ["👍", "❤️", "🎉", "🔥", "👏", "😄", "🙌", "💯"];

function timeAgo(iso: string): string {
  const s = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return "just now";
  const m = Math.floor(s / 60); if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60); if (h < 24) return `${h}h ago`;
  const d = Math.floor(h / 24); if (d < 7) return `${d}d ago`;
  return new Date(iso).toLocaleDateString();
}

/** Render body text with @mentions highlighted. */
function withMentions(body: string): ReactNode[] {
  return body.split(/(@[A-Za-z0-9._-]{2,32})/g).map((part, i) =>
    part.startsWith("@")
      ? <span key={i} className="text-brand-600 font-medium">{part}</span>
      : <span key={i}>{part}</span>);
}

/**
 * Pulse — the team social feed. Post updates, react with emoji, comment, and @mention teammates.
 * Polls so new posts/reactions/comments surface live without a refresh.
 */
export function PulsePage() {
  const me = useSelector((s: RootState) => s.auth.user);
  const { data: posts, isLoading } = useListFeedQuery({ take: 30 }, { pollingInterval: 20_000 });
  const [createPost, { isLoading: posting }] = useCreatePostMutation();
  const toast = useToast();
  const [draft, setDraft] = useState("");

  async function submit() {
    const body = draft.trim();
    if (!body) return;
    try {
      await createPost({ body }).unwrap();
      setDraft("");
    } catch (err: unknown) {
      toast.error("Couldn't post", getErrorDetail(err) ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader eyebrow="Team" title="Pulse"
        description="Share an update, celebrate a win, ask the floor. React, comment and @mention teammates." />

      {/* Composer */}
      <Card className="mb-5">
        <CardBody className="flex gap-3">
          <Avatar name={me?.userName ?? "You"} size={40} />
          <div className="flex-1 min-w-0">
            <Textarea
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => { if ((e.metaKey || e.ctrlKey) && e.key === "Enter") { e.preventDefault(); submit(); } }}
              placeholder="Share something with the team…  Use @name to mention someone."
              rows={draft ? 3 : 2}
            />
            <div className="flex items-center justify-between mt-2">
              <span className="text-xs text-ink-400">⌘/Ctrl + Enter to post</span>
              <Button size="sm" loading={posting} disabled={!draft.trim()} onClick={submit}
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
            description="Be the first to post — share a win, a shout-out, or an update for the team." />
        </CardBody></Card>
      ) : (
        <div className="space-y-4">
          {posts.map((p) => <PostCard key={p.id} post={p} meName={me?.userName ?? ""} />)}
        </div>
      )}
    </>
  );
}

function PostCard({ post, meName }: { post: FeedPost; meName: string }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [react] = useReactPostMutation();
  const [deletePost] = useDeletePostMutation();
  const [showPicker, setShowPicker] = useState(false);
  const [showComment, setShowComment] = useState(false);

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
    <Card className="hover:shadow-card-hover transition-shadow">
      <CardBody>
        <div className="flex items-start gap-3">
          <Avatar name={post.authorName} size={40} />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2">
              <span className="font-semibold text-ink-900">{post.authorName}</span>
              <span className="text-xs text-ink-400 tabular-nums">· {timeAgo(post.createdAt)}</span>
              {post.canDelete && (
                <button onClick={remove} aria-label="Delete post" title="Delete post"
                  className="ml-auto text-ink-300 hover:text-rose-500 transition-colors">
                  <Icon name="trash" size={14} />
                </button>
              )}
            </div>
            <p className="text-sm text-ink-800 mt-1 whitespace-pre-wrap break-words leading-relaxed">{withMentions(post.body)}</p>

            {/* Reactions */}
            <div className="flex items-center gap-1.5 flex-wrap mt-3">
              {post.reactions.map((r) => (
                <button key={r.emoji} onClick={() => toggle(r.emoji)}
                  title={r.mine ? "Remove your reaction" : "React"}
                  className={`inline-flex items-center gap-1 h-7 px-2 rounded-full border text-xs transition-colors ${
                    r.mine ? "border-brand-300 bg-brand-50 text-brand-700" : "border-ink-200 hover:bg-ink-50 text-ink-600"}`}>
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
                <CommentComposer postId={post.id} meName={meName} />
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

function CommentComposer({ postId, meName }: { postId: string; meName: string }) {
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
      <input
        value={body}
        onChange={(e) => setBody(e.target.value)}
        onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); send(); } }}
        placeholder="Write a comment…  @name to mention"
        aria-label="Write a comment"
        className="flex-1 h-9 rounded-full border border-ink-200 px-3.5 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20"
      />
      <Button size="sm" variant="ghost" loading={isLoading} disabled={!body.trim()} onClick={send}>Send</Button>
    </div>
  );
}
