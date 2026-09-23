import type { ReactNode } from "react";

/**
 * Renders a lesson body from the small markdown subset the curriculum is written in:
 * `##`/`###` headings, `-` bullets, `>` callouts, paragraphs, `**bold**` and `` `code` ``.
 *
 * Deliberately hand-written rather than pulling in a markdown library. The whole corpus is seeded
 * with the application, so the grammar is known and fixed; a general parser would mean a new
 * dependency and a raw-HTML path we have no use for. Nothing here ever produces HTML from the
 * source — every branch returns React elements, so lesson text cannot inject markup.
 */

/** Splits a line into bold / code / plain runs. Non-greedy so `**a** and **b**` stays two runs. */
function inline(text: string): ReactNode[] {
  const out: ReactNode[] = [];
  const pattern = /(\*\*[^*]+\*\*|`[^`]+`)/g;
  let last = 0;
  let m: RegExpExecArray | null;
  let i = 0;

  while ((m = pattern.exec(text)) !== null) {
    if (m.index > last) out.push(text.slice(last, m.index));
    const token = m[0];
    if (token.startsWith("**")) {
      out.push(<strong key={i++} className="font-semibold text-ink-900">{token.slice(2, -2)}</strong>);
    } else {
      out.push(
        <code key={i++} className="rounded bg-ink-100 px-1.5 py-0.5 text-[0.85em] font-mono text-ink-800">
          {token.slice(1, -1)}
        </code>,
      );
    }
    last = m.index + token.length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

type Block =
  | { kind: "h2" | "h3" | "p"; lines: string[] }
  | { kind: "ul" | "quote"; lines: string[] };

/** Groups consecutive lines into blocks, so a bullet list stays one list and a paragraph wraps. */
function parse(markdown: string): Block[] {
  const blocks: Block[] = [];
  let current: Block | null = null;

  const push = () => { if (current) { blocks.push(current); current = null; } };

  for (const raw of markdown.replace(/\r\n/g, "\n").split("\n")) {
    const line = raw.trim();

    if (!line) { push(); continue; }

    if (line.startsWith("### ")) { push(); blocks.push({ kind: "h3", lines: [line.slice(4)] }); continue; }
    if (line.startsWith("## "))  { push(); blocks.push({ kind: "h2", lines: [line.slice(3)] }); continue; }

    if (line.startsWith("- ")) {
      if (current?.kind !== "ul") { push(); current = { kind: "ul", lines: [] }; }
      current.lines.push(line.slice(2));
      continue;
    }
    if (line.startsWith(">")) {
      if (current?.kind !== "quote") { push(); current = { kind: "quote", lines: [] }; }
      current.lines.push(line.replace(/^>\s?/, ""));
      continue;
    }

    // A wrapped paragraph: the source hard-wraps, so join the run back into one flowing line.
    if (current?.kind !== "p") { push(); current = { kind: "p", lines: [] }; }
    current.lines.push(line);
  }
  push();
  return blocks;
}

export function LessonBody({ markdown }: { markdown: string }) {
  const blocks = parse(markdown);

  return (
    <div className="flex flex-col gap-4 text-[15px] leading-7 text-ink-700">
      {blocks.map((b, i) => {
        switch (b.kind) {
          case "h2":
            return (
              <h2 key={i} className="text-lg font-semibold text-ink-900 mt-2 first:mt-0">
                {inline(b.lines[0])}
              </h2>
            );
          case "h3":
            return (
              <h3 key={i} className="text-[15px] font-semibold text-ink-900 mt-1">
                {inline(b.lines[0])}
              </h3>
            );
          case "ul":
            return (
              <ul key={i} className="flex flex-col gap-2 pl-1">
                {b.lines.map((li, j) => (
                  <li key={j} className="flex gap-3">
                    <span aria-hidden className="mt-2.5 h-1.5 w-1.5 shrink-0 rounded-full bg-brand-400" />
                    <span>{inline(li)}</span>
                  </li>
                ))}
              </ul>
            );
          case "quote":
            return (
              <blockquote
                key={i}
                className="rounded-r-lg border-l-2 border-brand-400 bg-brand-50/60 px-4 py-3 text-ink-700"
              >
                {inline(b.lines.join(" "))}
              </blockquote>
            );
          default:
            return <p key={i}>{inline(b.lines.join(" "))}</p>;
        }
      })}
    </div>
  );
}
