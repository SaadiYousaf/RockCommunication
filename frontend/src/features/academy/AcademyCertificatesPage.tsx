import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { Link } from "react-router-dom";
import { Button, Card, CardBody, EmptyState, Icon, PageHeader, QueryState, Skeleton } from "../../shared/ui";
import { useAcademyCertificatesQuery } from "../../shared/api/baseApi";
import type { AcademyCertificate } from "../../shared/api/types";
import { ACADEMY_MSG as M } from "./messages";

/**
 * Certificates earned. Each one prints cleanly on its own — the browser's own print dialog is used
 * rather than generating a PDF server-side, which would mean a rendering dependency for something
 * people print perhaps twice.
 */

function formatIssued(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" });
}

function Certificate({ cert, onPrint }: { cert: AcademyCertificate; onPrint?: () => void }) {
  return (
    <Card
      elevated
      className="academy-certificate relative overflow-hidden print:break-inside-avoid print:shadow-none"
    >
      {/* A quiet guilloche-ish frame: two inset rules, nothing that fights the type. */}
      <div aria-hidden className="pointer-events-none absolute inset-3 rounded-xl border border-accent-200/70" />
      <div aria-hidden className="pointer-events-none absolute inset-[18px] rounded-lg border border-accent-100" />

      <CardBody className="relative flex flex-col items-center gap-4 px-8 py-10 text-center">
        <span className="grid h-12 w-12 place-items-center rounded-full bg-gradient-to-br from-accent-50 to-accent-100 text-accent-600 ring-1 ring-accent-200">
          <Icon name="star" className="h-6 w-6" />
        </span>

        <p className="text-xs uppercase tracking-[0.2em] text-ink-500">{M.certificateAwardedTo}</p>
        <p className="text-2xl font-semibold text-ink-900">{cert.learnerName}</p>
        <p className="text-sm text-ink-600">{M.certificateCompleted}</p>
        <p className="text-xl font-semibold text-brand-700">{cert.courseTitle}</p>

        <div className="mt-2 flex flex-wrap items-center justify-center gap-x-6 gap-y-1 text-xs text-ink-500">
          <span>{M.certificateIssued} {formatIssued(cert.issuedAt)}</span>
          <span className="font-mono tabular-nums">{M.certificateSerial} {cert.serialNumber}</span>
        </div>

        {onPrint && (
          <Button variant="ghost" size="sm" className="mt-2" onClick={onPrint}>
            <Icon name="doc" className="h-4 w-4" />
            {M.printCertificate}
          </Button>
        )}
      </CardBody>
    </Card>
  );
}

export function AcademyCertificatesPage() {
  const certs = useAcademyCertificatesQuery();
  const [printing, setPrinting] = useState<AcademyCertificate | null>(null);

  // Print once the chosen certificate has actually rendered into the sheet — calling print() in the
  // click handler would capture the page as it was a frame earlier.
  useEffect(() => {
    if (!printing) return;
    document.body.classList.add("printing-academy");
    const finish = () => setPrinting(null);
    window.addEventListener("afterprint", finish);
    const timer = window.setTimeout(() => window.print(), 50);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("afterprint", finish);
      document.body.classList.remove("printing-academy");
    };
  }, [printing]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={M.certificates}
        description={M.certificatesHint}
        breadcrumbs={[{ label: M.title, to: "/academy" }, { label: M.certificates }]}
      />

      <QueryState
        isLoading={certs.isLoading}
        isError={certs.isError}
        error={certs.error}
        isEmpty={(certs.data?.length ?? 0) === 0}
        resource={M.certificatesResourceName}
        onRetry={certs.refetch}
        loading={
          <div className="grid gap-5 lg:grid-cols-2">
            {Array.from({ length: 2 }).map((_, i) => <Skeleton key={i} className="h-72 rounded-2xl" />)}
          </div>
        }
        empty={
          <Card><EmptyState
            icon={<Icon name="star" className="h-6 w-6" />}
            title={M.noCertificatesTitle}
            description={M.noCertificatesDesc}
            tone="accent"
            action={<Link to="/academy"><Button variant="secondary">{M.yourPath}</Button></Link>}
          /></Card>
        }
      >
        <div className="grid gap-5 lg:grid-cols-2">
          {certs.data?.map((c) => (
            <Certificate key={c.serialNumber} cert={c} onPrint={() => setPrinting(c)} />
          ))}
        </div>
      </QueryState>

      <Link to="/academy" className="inline-flex items-center gap-1 text-sm text-ink-600 hover:text-ink-900 print:hidden">
        <Icon name="chevronLeft" className="h-4 w-4" />
        {M.title}
      </Link>

      {printing && createPortal(
        <div className="academy-print-sheet"><Certificate cert={printing} /></div>,
        document.body,
      )}
    </div>
  );
}
