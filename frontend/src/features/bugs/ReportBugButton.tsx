import { useState } from "react";
import { useLocation } from "react-router-dom";
import { getErrorDetail } from "../../shared/api/apiError";
import { useCreateBugMutation } from "../../shared/api/baseApi";
import { BUG_SEVERITIES } from "../../shared/constants/bugs";
import { Button, Icon, Input, Modal, Select, Textarea, useToast } from "../../shared/ui";

/**
 * App-wide "Report a bug" affordance: a small floating button, always reachable, that opens a
 * lightweight report form. The current route and browser are captured automatically so triagers
 * have reproduction context. Available to every signed-in user.
 */
export function ReportBugButton() {
  const location = useLocation();
  const toast = useToast();
  const [create, { isLoading }] = useCreateBugMutation();

  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState("Medium");

  function reset() { setTitle(""); setDescription(""); setSeverity("Medium"); }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim() || !description.trim()) return;
    try {
      await create({
        title: title.trim(),
        description: description.trim(),
        severity,
        pageUrl: location.pathname + location.search,
        userAgent: navigator.userAgent,
      }).unwrap();
      setOpen(false);
      reset();
      toast.success("Thanks — bug reported", "You can track its status under Bugs.");
    } catch (err: unknown) {
      toast.error("Couldn't submit the report", getErrorDetail(err) ?? "Please try again.");
    }
  }

  return (
    <>
      <button
        type="button" onClick={() => setOpen(true)}
        aria-label="Report a bug" title="Report a bug"
        className="group fixed bottom-5 right-5 z-30 inline-flex items-center gap-2 rounded-full bg-ink-900/95 text-white shadow-pop
                   pl-3 pr-4 h-11 backdrop-blur-sm animate-fade-up
                   transition-[transform,background-color] duration-200 ease-out-quint hover:bg-ink-800 hover:-translate-y-0.5
                   focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
        style={{ marginBottom: "env(safe-area-inset-bottom)" }}
      >
        <Icon name="alert" size={16} className="text-amber-300 transition-transform group-hover:scale-110" />
        <span className="text-sm font-medium hidden sm:inline">Report a bug</span>
      </button>

      <Modal open={open} onClose={() => setOpen(false)} title="Report a bug"
        description="Tell us what went wrong. The page you're on is attached automatically."
        footer={<>
          <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
          <Button form="bug-form" type="submit" loading={isLoading} disabled={!title.trim() || !description.trim()}
            leftIcon={<Icon name="send" size={14} />}>Submit report</Button>
        </>}>
        <form id="bug-form" onSubmit={submit} className="space-y-4">
          <Input label="What's the problem?" value={title} onChange={(e) => setTitle(e.target.value)}
            placeholder="Short summary — e.g. “Payroll slip shows wrong net pay”" maxLength={200} required autoFocus />
          <Textarea label="Details" value={description} onChange={(e) => setDescription(e.target.value)}
            rows={5} maxLength={5000} required
            placeholder="What did you do, what did you expect, and what happened instead? Steps to reproduce help a lot." />
          <Select label="Severity" value={severity} onChange={(e) => setSeverity(e.target.value)}>
            {BUG_SEVERITIES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </Select>
          <p className="text-xs text-ink-400 flex items-center gap-1.5">
            <Icon name="info" size={12} /> Attaching: <span className="font-mono text-ink-500">{location.pathname}</span>
          </p>
        </form>
      </Modal>
    </>
  );
}
