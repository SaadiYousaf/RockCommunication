import { Link } from "react-router-dom";
import { useCloserQueueQuery } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR,
} from "../../shared/ui";

/** Closer work queue — verified leads awaiting a closing application. */
export function CloseQueuePage() {
  const { data: queue, isLoading } = useCloserQueueQuery();
  return (
    <>
      <PageHeader title="Closer Queue" description="Verified leads ready to close. Open a lead to complete the application." />
      <Card>
        <CardHeader title="Ready to close" subtitle={queue ? `${queue.length} lead(s)` : undefined} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !queue || queue.length === 0 ? (
            <EmptyState icon={<Icon name="inbox" size={20} />} title="No verified leads" description="Verified leads will appear here." />
          ) : (
            <Table>
              <THead>
                <TR><TH>Name</TH><TH>Phone</TH><TH>Location</TH><TH>Age</TH><TH>Application</TH><TH></TH></TR>
              </THead>
              <TBody>
                {queue.map((l) => (
                  <TR key={l.id}>
                    <TD className="font-medium text-ink-900">{l.firstName} {l.lastName}</TD>
                    <TD className="font-mono text-xs">{l.phoneNumber}</TD>
                    <TD className="text-sm text-ink-600">{[l.city, l.state].filter(Boolean).join(", ") || "—"}</TD>
                    <TD className="text-sm">{l.ageYears ?? "—"}</TD>
                    <TD>{l.hasApplication ? <Badge tone="info" variant="soft">Started</Badge> : <Badge tone="neutral" variant="soft">New</Badge>}</TD>
                    <TD className="text-right">
                      <Link to={`/close-queue/${l.id}`}>
                        <Button size="sm" leftIcon={<Icon name="briefcase" size={14} />}>Open</Button>
                      </Link>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>
    </>
  );
}
