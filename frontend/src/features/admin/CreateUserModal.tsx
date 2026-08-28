import { useMemo, useState } from "react";
import {
  useRegisterMutation, useListCallCentersQuery, useListAgenciesQuery, useOrgTreeQuery,
} from "../../shared/api/baseApi";
import { Button, Input, Modal, Select, useToast } from "../../shared/ui";
import { ALL_ROLES, roleLabel } from "../../shared/constants/roles";
import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { ADMIN_MSG } from "./messages";

/**
 * Create a user AND place them in the org in one step.
 *
 * WHY: creating an account used to be a separate page that captured only email, username, roles and
 * agency. The admin then had to find the new row in the list and set its call centre and team as two
 * more actions — and a user with neither is effectively invisible to most of the app, so this was
 * missed constantly. Everything that decides what a user can see is now on one form.
 */
export function CreateUserModal({
  open, onClose, isSuperAdmin, defaultAgencyId,
}: {
  open: boolean;
  onClose: () => void;
  isSuperAdmin: boolean;
  /** The signed-in admin's own agency; used when they cannot choose one. */
  defaultAgencyId: string | null | undefined;
}) {
  const toast = useToast();
  const [register, { isLoading: saving }] = useRegisterMutation();

  const { data: agencies } = useListAgenciesQuery(undefined, { skip: !isSuperAdmin });
  const { data: callCenters } = useListCallCentersQuery();
  const { data: org } = useOrgTreeQuery(undefined);

  const [email, setEmail] = useState("");
  const [userName, setUserName] = useState("");
  const [agencyId, setAgencyId] = useState(defaultAgencyId ?? "");
  const [callCenterId, setCallCenterId] = useState("");
  const [teamId, setTeamId] = useState("");
  const [roles, setRoles] = useState<string[]>([]);
  // Empty means "send them an invitation with a temporary password" — the normal path.
  const [password, setPassword] = useState("");

  const effectiveAgencyId = isSuperAdmin ? agencyId : (defaultAgencyId ?? "");

  // Only offer centres that belong to the agency being created into; the server rejects the rest.
  const centres = useMemo(
    () => (callCenters ?? []).filter((c) => !c.agencyId || c.agencyId === effectiveAgencyId),
    [callCenters, effectiveAgencyId],
  );

  // The org tree we hold covers one agency, so its teams only apply when that is the target.
  const teams = useMemo(
    () => (org && (!org.agencyId || org.agencyId === effectiveAgencyId) ? org.teams ?? [] : []),
    [org, effectiveAgencyId],
  );

  function toggleRole(r: string) {
    setRoles((prev) => (prev.includes(r) ? prev.filter((x) => x !== r) : [...prev, r]));
  }

  function reset() {
    setEmail(""); setUserName(""); setCallCenterId(""); setTeamId("");
    setRoles([]); setPassword("");
    setAgencyId(defaultAgencyId ?? "");
  }

  async function submit() {
    if (!email.trim() || !userName.trim()) {
      toast.error(ADMIN_MSG.userMgmt.createMissingBasics);
      return;
    }
    if (!effectiveAgencyId) {
      toast.error(ADMIN_MSG.userMgmt.createNeedsAgency);
      return;
    }
    try {
      await register({
        email: email.trim(),
        userName: userName.trim(),
        password: password.trim() ? password.trim() : null,
        agencyId: effectiveAgencyId,
        roles,
        callCenterId: callCenterId || null,
        teamId: teamId || null,
      }).unwrap();
      toast.success(
        ADMIN_MSG.userMgmt.userCreated(userName.trim()),
        password.trim() ? ADMIN_MSG.userMgmt.userCreatedWithPassword : ADMIN_MSG.userMgmt.userCreatedInvited,
      );
      reset();
      onClose();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.userMgmt.createFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={ADMIN_MSG.userMgmt.createTitle}
      description={ADMIN_MSG.userMgmt.createDescription}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>{ADMIN_MSG.userMgmt.createCancel}</Button>
          <Button onClick={submit} loading={saving}>{ADMIN_MSG.userMgmt.createSubmit}</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid sm:grid-cols-2 gap-3">
          <Input
            label={ADMIN_MSG.userMgmt.fieldUserName} required
            placeholder="e.g. sara.khan"
            value={userName} onChange={(e) => setUserName(e.target.value)}
          />
          <Input
            label={ADMIN_MSG.userMgmt.fieldEmail} type="email" required
            placeholder="name@example.com"
            value={email} onChange={(e) => setEmail(e.target.value)}
          />
        </div>

        {isSuperAdmin && (
          <div>
            <label className="block text-xs font-medium text-ink-600 mb-1">{ADMIN_MSG.userMgmt.fieldAgency}</label>
            <Select value={agencyId} onChange={(e) => {
              setAgencyId(e.target.value);
              // Centres and teams belong to an agency — a stale pick would be rejected server-side.
              setCallCenterId(""); setTeamId("");
            }}>
              <option value="">{ADMIN_MSG.userMgmt.selectAgency}</option>
              {(agencies ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </Select>
          </div>
        )}

        <div className="grid sm:grid-cols-2 gap-3">
          <div>
            <label className="block text-xs font-medium text-ink-600 mb-1">{ADMIN_MSG.userMgmt.fieldCallCenter}</label>
            <Select value={callCenterId} onChange={(e) => setCallCenterId(e.target.value)}>
              <option value="">{ADMIN_MSG.userMgmt.agencyLevel}</option>
              {centres.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </div>
          <div>
            <label className="block text-xs font-medium text-ink-600 mb-1">{ADMIN_MSG.userMgmt.fieldTeam}</label>
            <Select value={teamId} onChange={(e) => setTeamId(e.target.value)}>
              <option value="">{ADMIN_MSG.userMgmt.noTeam}</option>
              {teams.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </Select>
          </div>
        </div>

        <div>
          <label className="block text-xs font-medium text-ink-600 mb-1.5">{ADMIN_MSG.userMgmt.fieldRoles}</label>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
            {ALL_ROLES.map((r) => {
              const on = roles.includes(r);
              return (
                <button
                  key={r}
                  type="button"
                  onClick={() => toggleRole(r)}
                  aria-pressed={on}
                  className={
                    "text-left text-xs rounded-lg border px-2.5 py-2 transition " +
                    (on
                      ? "border-brand-300 bg-brand-50 text-brand-800 font-medium"
                      : "border-ink-200 hover:bg-ink-50 text-ink-600")
                  }
                >
                  {roleLabel(r)}
                </button>
              );
            })}
          </div>
          <p className="text-xs text-ink-500 mt-1.5">{ADMIN_MSG.userMgmt.rolesHint}</p>
        </div>

        <div>
          <Input
            label={ADMIN_MSG.userMgmt.fieldPassword}
            type="text"
            placeholder={ADMIN_MSG.userMgmt.passwordPlaceholder}
            value={password} onChange={(e) => setPassword(e.target.value)}
          />
          <p className="text-xs text-ink-500 mt-1.5">{ADMIN_MSG.userMgmt.passwordHint}</p>
        </div>
      </div>
    </Modal>
  );
}
