import { useEffect, type ReactNode } from "react";
import { cn } from "./cn";

const APP_NAME = "Rock Communication";

function titleToString(t: ReactNode): string | null {
  if (typeof t === "string" || typeof t === "number") return String(t);
  return null;
}

/**
 * Page hero. Ties every screen to the same vertical rhythm:
 *   eyebrow → breadcrumbs → title (+ inline badge) → description → actions
 *   ──────────────────────  hairline divider  ─────────────────────
 *   optional tabs row
 *
 * Visual touches that lift it out of "just a heading":
 *   • A 3px brand→accent gradient bar to the left of the title
 *   • A subtle hairline divider underneath that anchors the section break
 *   • Animated entrance (fade-up) so navigating between pages feels intentional
 */
export function PageHeader({
  title, description, breadcrumbs, actions, eyebrow, tabs, badge, className,
}: {
  title: ReactNode;
  description?: ReactNode;
  breadcrumbs?: { label: string; to?: string }[];
  actions?: ReactNode;
  eyebrow?: ReactNode;
  tabs?: ReactNode;
  badge?: ReactNode;
  className?: string;
}) {
  useEffect(() => {
    const t = titleToString(title);
    if (!t) return;
    const prev = document.title;
    document.title = `${t} · ${APP_NAME}`;
    return () => { document.title = prev; };
  }, [title]);

  return (
    <header className={cn("animate-fade-up flex flex-col gap-3 mb-7", className)}>
      {breadcrumbs && breadcrumbs.length > 0 && (
        <nav className="flex items-center gap-1.5 text-xs text-ink-500" aria-label="Breadcrumb">
          {breadcrumbs.map((b, i) => (
            <span key={i} className="flex items-center gap-1.5">
              {b.to
                ? <a href={b.to} className="hover:text-ink-800 transition-colors">{b.label}</a>
                : <span className="text-ink-700 font-medium">{b.label}</span>}
              {i < breadcrumbs.length - 1 && (
                <svg width="10" height="10" viewBox="0 0 12 12" className="text-ink-300" aria-hidden>
                  <path d="M4 2.5 L8 6 L4 9.5" stroke="currentColor" strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              )}
            </span>
          ))}
        </nav>
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="min-w-0 relative pl-4 sm:pl-5">
          {/* Brand-to-accent rule on the left */}
          <span aria-hidden
            className="absolute left-0 top-1 bottom-1 w-[3px] rounded-full bg-gradient-to-b from-brand-500 via-brand-600 to-accent-500" />

          {eyebrow && (
            <div className="section-title mb-1.5 flex items-center gap-1.5">
              <span aria-hidden className="inline-block h-1 w-1 rounded-full bg-brand-500" />
              <span>{eyebrow}</span>
            </div>
          )}
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-[26px] sm:text-[30px] leading-[1.15] font-semibold tracking-tight text-ink-900">
              {title}
            </h1>
            {badge}
          </div>
          {description && (
            <p className="text-sm sm:text-[14px] text-ink-500 mt-2 max-w-2xl leading-relaxed">{description}</p>
          )}
        </div>
        {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
      </div>

      {/* Section break — gives every page the same visual anchor under the hero */}
      <div className="divider-soft mt-1" />

      {tabs && <div className="-mb-1">{tabs}</div>}
    </header>
  );
}
