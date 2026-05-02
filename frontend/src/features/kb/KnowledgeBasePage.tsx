import { useState } from "react";
import { useGetKbArticleQuery, useSearchKbQuery, useUpsertKbArticleMutation } from "../../shared/api/baseApi";
import {
  Badge, Button, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Textarea, useToast, cn,
} from "../../shared/ui";

export function KnowledgeBasePage() {
  const [q, setQ] = useState("");
  const [activeSlug, setActiveSlug] = useState<string | null>(null);
  const { data: results, isLoading: searching } = useSearchKbQuery({ q: q || undefined });
  const { data: article } = useGetKbArticleQuery(activeSlug!, { skip: !activeSlug });
  const [upsert, { isLoading: saving }] = useUpsertKbArticleMutation();
  const [editing, setEditing] = useState<any | null>(null);
  const toast = useToast();

  function openNew() {
    setEditing({ id: null, slug: "", title: "", body: "", tags: "", category: "", isPublished: false });
  }

  async function handleSave(a: any) {
    try {
      await upsert(a).unwrap();
      toast.success("Article saved", a.title);
      setEditing(null);
      if (a.slug) setActiveSlug(a.slug);
    } catch (err: any) {
      toast.error("Couldn't save article", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Knowledge Base"
        description="Searchable team knowledge — talking points, FAQs, scripts, and onboarding."
        actions={<Button leftIcon={<Icon name="plus" size={16} />} onClick={openNew}>New article</Button>}
      />

      <div className="grid grid-cols-12 gap-5 h-[calc(100vh-14rem)]">
        {/* List */}
        <aside className="col-span-12 lg:col-span-4 surface flex flex-col overflow-hidden">
          <div className="p-4 border-b hairline">
            <Input
              leftIcon={<Icon name="search" size={16} />}
              placeholder="Search articles..."
              value={q} onChange={(e) => setQ(e.target.value)}
            />
          </div>
          <div className="flex-1 overflow-y-auto">
            {searching ? (
              <div className="p-4 space-y-2">
                {[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-16" />)}
              </div>
            ) : !results || results.length === 0 ? (
              <div className="p-6">
                <EmptyState
                  icon={<Icon name="doc" size={18} />}
                  title="No articles"
                  description={q ? `No results for "${q}"` : "Get started by creating your first article."}
                  action={<Button size="sm" leftIcon={<Icon name="plus" size={14} />} onClick={openNew}>New article</Button>}
                />
              </div>
            ) : (
              <ul className="p-2 space-y-1">
                {results.map((a: any) => {
                  const active = activeSlug === a.slug;
                  return (
                    <li key={a.id}>
                      <button
                        className={cn(
                          "w-full text-left px-3 py-2.5 rounded-lg transition-colors",
                          active ? "bg-brand-50 ring-1 ring-brand-200"
                                 : "hover:bg-ink-50",
                        )}
                        onClick={() => setActiveSlug(a.slug)}
                      >
                        <div className={cn("font-medium truncate", active ? "text-brand-700" : "text-ink-900")}>
                          {a.title}
                        </div>
                        <div className="text-xs text-ink-500 mt-0.5 flex items-center gap-2">
                          {a.category && <Badge tone="neutral" variant="soft">{a.category}</Badge>}
                          <span>{a.viewCount ?? 0} views</span>
                          {!a.isPublished && <Badge tone="warning" variant="soft">Draft</Badge>}
                        </div>
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </aside>

        {/* Reader */}
        <main className="col-span-12 lg:col-span-8 surface overflow-hidden flex flex-col">
          {article ? (
            <>
              <div className="p-6 border-b hairline">
                <div className="flex items-start justify-between gap-4">
                  <div className="min-w-0">
                    <h2 className="text-2xl font-semibold tracking-tight text-ink-900">{article.title}</h2>
                    <div className="flex flex-wrap items-center gap-2 mt-2 text-xs text-ink-500">
                      {article.category && <Badge tone="brand" variant="soft">{article.category}</Badge>}
                      {article.tags && article.tags.split(",").map((t: string) => t.trim()).filter(Boolean).map((tag: string) => (
                        <Badge key={tag} tone="neutral" variant="outline">#{tag}</Badge>
                      ))}
                      {!article.isPublished && <Badge tone="warning" variant="soft">Draft</Badge>}
                      <span>· {article.viewCount ?? 0} views</span>
                    </div>
                  </div>
                  <Button variant="outline" size="sm" leftIcon={<Icon name="cog" size={14} />}
                    onClick={() => setEditing(article)}>Edit</Button>
                </div>
              </div>
              <div className="flex-1 overflow-y-auto p-6">
                <div className="prose prose-sm max-w-none text-ink-800">
                  <pre className="whitespace-pre-wrap font-sans text-sm leading-relaxed text-ink-800">
                    {article.body}
                  </pre>
                </div>
              </div>
            </>
          ) : (
            <div className="flex-1 grid place-items-center">
              <EmptyState
                icon={<Icon name="doc" size={20} />}
                title="Pick an article to read"
                description="Search or browse the list to read knowledge base entries."
              />
            </div>
          )}
        </main>
      </div>

      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={editing?.id ? "Edit article" : "New article"}
        size="xl"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
            <Button loading={saving} onClick={() => handleSave(editing)}>Save article</Button>
          </>
        }
      >
        {editing && (
          <div className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Input label="Slug" required placeholder="lower-case-with-hyphens"
                value={editing.slug} onChange={(e) => setEditing({ ...editing, slug: e.target.value })} />
              <Input label="Category" placeholder="Onboarding, FAQ, ..."
                value={editing.category ?? ""} onChange={(e) => setEditing({ ...editing, category: e.target.value })} />
            </div>
            <Input label="Title" required value={editing.title}
              onChange={(e) => setEditing({ ...editing, title: e.target.value })} />
            <Input label="Tags" placeholder="comma, separated, tags"
              value={editing.tags ?? ""} onChange={(e) => setEditing({ ...editing, tags: e.target.value })} />
            <Textarea label="Body" required value={editing.body}
              onChange={(e) => setEditing({ ...editing, body: e.target.value })}
              className="font-mono text-sm min-h-[260px]" />
            <label className="inline-flex items-center gap-2 text-sm text-ink-700">
              <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
                checked={!!editing.isPublished}
                onChange={(e) => setEditing({ ...editing, isPublished: e.target.checked })} />
              Published
            </label>
          </div>
        )}
      </Modal>
    </>
  );
}
