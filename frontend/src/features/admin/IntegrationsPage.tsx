import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import {
  useCheckIntegrationMutation, useListIntegrationsQuery,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, PageHeader,
  Skeleton, useToast, cn, type IconName,
} from "../../shared/ui";
import type { IntegrationInfo, IntegrationHealthResult } from "../../shared/api/types";

const integrationIcon: Record<string, IconName> = {
  jornaya:  "shield",
  dialer:   "phone",
  sms:      "chat",
  email:    "doc",
  carriers: "briefcase",
  funding:  "briefcase",
  bla:      "target",
  trello:   "list",
};

const integrationTone: Record<string, string> = {
  jornaya:  "bg-brand-50 text-brand-600",
  dialer:   "bg-emerald-50 text-emerald-600",
  sms:      "bg-brand-50 text-brand-600",
  email:    "bg-amber-50 text-amber-600",
  carriers: "bg-violet-50 text-violet-600",
  funding:  "bg-rose-50 text-rose-600",
  bla:      "bg-ink-100 text-ink-700",
  trello:   "bg-ink-100 text-ink-700",
};

export function IntegrationsPage() {
  const { data: items, isLoading, refetch } = useListIntegrationsQuery();
  const [check] = useCheckIntegrationMutation();
  const toast = useToast();
  const [results, setResults] = useState<Record<string, IntegrationHealthResult>>({});
  const [checking, setChecking] = useState<string | null>(null);

  async function runCheck(code: string) {
    setChecking(code);
    try {
      const res = await check(code).unwrap();
      setResults((r) => ({ ...r, [code]: res }));
      if (res.healthy) toast.success(`${code} OK`, res.message);
      else toast.error(`${code} unhealthy`, res.message);
    } catch (err: unknown) {
      toast.error("Check failed", getErrorDetail(err) ?? "Try again.");
    } finally {
      setChecking(null);
    }
  }

  const stats = items
    ? {
        total: items.length,
        live:  items.filter((i) => i.active).length,
        stub:  items.filter((i) => i.usingStub).length,
      }
    : null;

  return (
    <>
      <PageHeader
        title="Integrations"
        description="Centralized view of every external provider — Jornaya, Vici, SMS, Email, Carriers, Funding, BLA, Trello. Flip any from Stub → Live in appsettings.json."
        breadcrumbs={[{ label: "Admin" }, { label: "Integrations" }]}
        actions={
          <Button variant="outline" leftIcon={<Icon name="filter" size={16} />} onClick={() => refetch()}>
            Refresh
          </Button>
        }
      />

      {stats && (
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
          <SmallTile label="Configured" value={stats.total} icon="cog"   tone="bg-brand-50 text-brand-600" />
          <SmallTile label="Live"       value={stats.live}  icon="check" tone="bg-emerald-50 text-emerald-600" />
          <SmallTile label="On Stubs"   value={stats.stub}  icon="shield" tone="bg-amber-50 text-amber-600" />
        </div>
      )}

      {isLoading ? (
        <div className="space-y-3">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-32" />)}</div>
      ) : !items || items.length === 0 ? (
        <Card><CardBody>
          <EmptyState icon={<Icon name="cog" size={20} />} title="No integrations" description="Nothing configured." />
        </CardBody></Card>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          {items.map((it) => (
            <IntegrationCard
              key={it.code}
              info={it}
              result={results[it.code]}
              checking={checking === it.code}
              onCheck={() => runCheck(it.code)}
            />
          ))}
        </div>
      )}

      <Card className="mt-6 bg-ink-50/40">
        <CardBody className="flex items-start gap-4">
          <div className="h-10 w-10 rounded-lg bg-brand-50 text-brand-600 grid place-items-center shrink-0">
            <Icon name="shield" size={18} />
          </div>
          <div>
            <div className="font-semibold text-ink-900">How to go live</div>
            <p className="text-sm text-ink-600 mt-1 leading-relaxed">
              Each provider can run as a stub or a real HTTP client. Edit{" "}
              <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">
                appsettings.json
              </code>{" "}
              (or env vars), set{" "}
              <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">
                Provider
              </code>{" "}
              to <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">Http</code>{" "}
              (or the named provider — <code className="font-mono">Vici</code>, <code className="font-mono">Twilio</code>,{" "}
              <code className="font-mono">GHL</code>, <code className="font-mono">Smtp</code>) and supply credentials. Restart the API.
            </p>
          </div>
        </CardBody>
      </Card>
    </>
  );
}

function IntegrationCard({
  info, result, checking, onCheck,
}: {
  info: IntegrationInfo;
  result?: IntegrationHealthResult;
  checking: boolean;
  onCheck: () => void;
}) {
  const icon = integrationIcon[info.code] ?? "cog";
  const tone = integrationTone[info.code] ?? "bg-ink-100 text-ink-700";

  return (
    <Card className="hover:shadow-card-hover transition-shadow">
      <CardHeader
        title={
          <div className="flex items-center gap-3">
            <div className={cn("h-10 w-10 rounded-xl grid place-items-center", tone)}>
              <Icon name={icon} size={18} />
            </div>
            <div>
              <div className="text-base font-semibold text-ink-900">{info.name}</div>
              <code className="text-[11px] font-mono text-ink-500">{info.code}</code>
            </div>
          </div>
        }
        action={
          info.active
            ? <Badge tone="success" variant="soft" dot>Live</Badge>
            : <Badge tone="warning" variant="soft" dot>Stub</Badge>
        }
      />
      <CardBody className="pt-0">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-2 text-sm">
          <Row label="Mode" value={
            <Badge tone={info.active ? "success" : "neutral"} variant="soft">
              {info.mode}
            </Badge>
          } />
          <Row label="Provider" value={<code className="text-xs font-mono text-ink-700">{info.provider}</code>} />
          {info.fields.map((f, i) => (
            <Row
              key={i}
              label={f.label}
              value={
                f.value
                  ? <span className={cn("text-sm", f.masked ? "font-mono text-ink-700" : "text-ink-700")}>
                      {f.value}
                    </span>
                  : <span className="text-ink-400 text-xs">— not set —</span>
              }
            />
          ))}
        </div>

        <div className="mt-4 pt-3 border-t hairline flex items-center justify-between gap-3">
          <div className="flex-1 min-w-0">
            {result && (
              <div className={cn(
                "text-xs flex items-center gap-2",
                result.healthy ? "text-emerald-700" : "text-rose-700",
              )}>
                <span className="h-2 w-2 rounded-full bg-current" />
                <span className="font-medium">{result.healthy ? "Healthy" : "Unhealthy"}</span>
                <span className="text-ink-500 truncate">· {result.message}</span>
                <span className="text-ink-400 font-mono">· {result.elapsedMs}ms</span>
              </div>
            )}
          </div>
          <Button
            variant="outline" size="sm"
            loading={checking}
            onClick={onCheck}
            leftIcon={<Icon name="check" size={14} />}
          >
            {result ? "Re-check" : "Check"}
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1 border-b hairline last:border-0 sm:border-0">
      <div className="text-xs font-medium text-ink-500">{label}</div>
      <div className="min-w-0 truncate text-right">{value}</div>
    </div>
  );
}

function SmallTile({ label, value, icon, tone }: { label: string; value: number; icon: IconName; tone: string }) {
  return (
    <div className="surface p-4 flex items-center gap-3">
      <div className={`h-10 w-10 rounded-lg grid place-items-center ${tone}`}>
        <Icon name={icon} size={18} />
      </div>
      <div>
        <div className="text-xs font-medium text-ink-500 uppercase tracking-wide">{label}</div>
        <div className="text-xl font-semibold text-ink-900">{value}</div>
      </div>
    </div>
  );
}
