import { useDispatch, useSelector } from "react-redux";
import { Button, Card, CardBody, Icon } from "../../shared/ui";
import { BrandLogo } from "../../shared/components/BrandLogo";
import { BRAND } from "../../shared/constants/brand";
import { clearAuth, setNetworkBlocked } from "../../app/authSlice";
import type { RootState } from "../../app/store";
import { AUTH_MSG } from "./messages";

/**
 * Shown when the server refuses the network this person is on.
 *
 * A full screen rather than a toast, because every request fails the same way — the app behind it
 * would be an empty shell, and the one sentence that explains why would be buried under a dozen
 * identical error toasts.
 *
 * The address is shown prominently and is selectable: the only way out of this screen is for the
 * user to tell an administrator which address to approve, so it has to be easy to copy and read
 * aloud down a phone line.
 */
export function NetworkBlockedPage() {
  const dispatch = useDispatch();
  const address = useSelector((s: RootState) => s.auth.networkBlockedAddress) || "";

  return (
    <div className="min-h-screen grid place-items-center p-6 bg-ink-50">
      <Card elevated className="max-w-lg w-full">
        <CardBody className="flex flex-col items-center text-center gap-5 py-10">
          <BrandLogo variant="mark" size={44} />

          <span className="grid h-12 w-12 place-items-center rounded-2xl bg-amber-50 text-amber-600 ring-1 ring-amber-200">
            <Icon name="shield" size={22} />
          </span>

          <div>
            <h1 className="text-xl font-semibold text-ink-900">{AUTH_MSG.networkBlockedTitle}</h1>
            <p className="mt-2 text-sm leading-6 text-ink-600 max-w-sm mx-auto">
              {AUTH_MSG.networkBlockedBody(BRAND.name)}
            </p>
          </div>

          {address && (
            <div className="w-full rounded-xl border border-ink-200 bg-white px-4 py-3">
              <div className="text-[11px] uppercase tracking-[0.16em] text-ink-500">
                {AUTH_MSG.networkBlockedAddressLabel}
              </div>
              {/* Selectable and monospaced — this is the value they have to read out or paste. */}
              <div className="mt-1 select-all font-mono text-base font-semibold text-ink-900 break-all">
                {address}
              </div>
            </div>
          )}

          <div className="flex flex-wrap items-center justify-center gap-3">
            {/* Retry, for the ordinary case: they moved onto the office network, or it was just
                approved, and nothing about their session needs to change. */}
            <Button
              variant="secondary"
              onClick={() => { dispatch(setNetworkBlocked(null)); window.location.reload(); }}
            >
              <Icon name="refresh" size={15} />
              {AUTH_MSG.networkBlockedRetry}
            </Button>
            <Button variant="ghost" onClick={() => dispatch(clearAuth())}>
              {AUTH_MSG.networkBlockedSignOut}
            </Button>
          </div>
        </CardBody>
      </Card>
    </div>
  );
}
