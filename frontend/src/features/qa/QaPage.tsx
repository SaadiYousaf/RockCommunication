import { useState } from "react";
import { useCreateRubricMutation, useRubricsQuery } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Textarea, useToast,
} from "../../shared/ui";

export function QaPage() {
  const { data: rubrics, isLoading } = useRubricsQuery();
  const [create, { isLoading: creating }] = useCreateRubricMutation();
  const toast = useToast();

  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [items, setItems] = useState([{ label: "Greeting", maxScore: 10, order: 1 }]);

  function addItem() { setItems((i) => [...i, { label: "", maxScore: 10, order: i.length + 1 }]); }
  function removeItem(idx: number) { setItems((i) => i.filter((_, n) => n !== idx)); }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await create({ name, description: description || undefined, items }).unwrap();
      toast.success("Rubric created", name);
      setName(""); setDescription(""); setItems([{ label: "Greeting", maxScore: 10, order: 1 }]);
      setOpen(false);
    } catch (err: any) {
      toast.error("Couldn't create rubric", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Quality Assurance"
        description="Define scoring rubrics that QA reviewers use to evaluate calls."
        actions={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New rubric</Button>}
      />

      {isLoading ? (
        <div className="space-y-3">
          {[0, 1, 2].map((i) => <Skeleton key={i} className="h-32" />)}
        </div>
      ) : !rubrics || rubrics.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="star" size={20} />}
            title="No rubrics yet"
            description="Create a rubric to standardize how your team scores calls."
            action={<Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>New rubric</Button>}
          />
        </CardBody></Card>
      ) : (
        <div className="space-y-4">
          {rubrics.map((r) => {
            const total = r.items.reduce((s, it) => s + it.maxScore, 0);
            return (
              <Card key={r.id}>
                <CardHeader
                  title={
                    <span className="flex items-center gap-2.5">
                      {r.name}
                      {r.isActive
                        ? <Badge tone="success" variant="soft" dot>Active</Badge>
                        : <Badge tone="neutral" variant="soft">Inactive</Badge>}
                    </span>
                  }
                  subtitle={r.description}
                  action={
                    <div className="text-right">
                      <div className="text-2xl font-semibold tracking-tight text-ink-900">{total}</div>
                      <div className="text-xs text-ink-500 uppercase tracking-wider">max points</div>
                    </div>
                  }
                />
                <CardBody className="pt-0">
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
                    {r.items.map((it, idx) => (
                      <div key={it.id} className="flex items-center gap-3 p-3 rounded-lg border hairline">
                        <div className="h-7 w-7 rounded-md bg-brand-50 text-brand-600 grid place-items-center text-xs font-semibold">
                          {idx + 1}
                        </div>
                        <div className="flex-1 min-w-0 text-sm text-ink-800 truncate">{it.label}</div>
                        <div className="text-xs font-mono text-ink-500">/ {it.maxScore}</div>
                      </div>
                    ))}
                  </div>
                </CardBody>
              </Card>
            );
          })}
        </div>
      )}

      <Modal
        open={open} onClose={() => setOpen(false)}
        title="New rubric"
        description="Add the criteria that reviewers will score against."
        size="xl"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="rubric-form" type="submit" loading={creating}>Create rubric</Button>
          </>
        }
      >
        <form id="rubric-form" onSubmit={submit} className="space-y-4">
          <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Inbound Sales Call" />
          <Textarea label="Description" value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="When to use this rubric..." />

          <div>
            <div className="text-xs font-medium text-ink-700 mb-1.5">Scoring items</div>
            <div className="space-y-2">
              {items.map((item, idx) => (
                <div key={idx} className="flex items-end gap-2">
                  <div className="h-8 w-8 rounded-md bg-brand-50 text-brand-600 grid place-items-center text-xs font-semibold mb-2.5">
                    {idx + 1}
                  </div>
                  <Input
                    placeholder="Item label (e.g. Used customer name)"
                    value={item.label}
                    onChange={(e) => setItems((arr) => arr.map((x, n) => n === idx ? { ...x, label: e.target.value } : x))}
                    containerClassName="flex-1"
                    required
                  />
                  <Input
                    type="number" min={1}
                    value={item.maxScore}
                    onChange={(e) => setItems((arr) => arr.map((x, n) => n === idx ? { ...x, maxScore: parseInt(e.target.value) || 0 } : x))}
                    containerClassName="w-24"
                    label={idx === 0 ? "Max" : undefined}
                  />
                  {items.length > 1 && (
                    <Button type="button" variant="ghost" size="icon" className="text-rose-600 hover:bg-rose-50 mb-0.5"
                      onClick={() => removeItem(idx)}>
                      <Icon name="x" size={14} />
                    </Button>
                  )}
                </div>
              ))}
            </div>
            <Button type="button" variant="outline" size="sm" className="mt-3" leftIcon={<Icon name="plus" size={14} />}
              onClick={addItem}>Add scoring item</Button>
          </div>
        </form>
      </Modal>
    </>
  );
}
