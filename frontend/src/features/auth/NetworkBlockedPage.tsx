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
/**
 * What an administrator should actually put on the allowlist.
 *
 * For IPv4 it is the address itself. For IPv6 it is the /64 PREFIX, because the host half of a
 * home or office IPv6 address rotates — privacy extensions change it roughly daily. Someone who
 * reads the full address off this screen and allowlists it gets in today and is locked out
 * tomorrow, which is a far worse experience than being locked out plainly.
 */
function approvableForm(address: string): { value: string; isPrefix: boolean } {
  if (!address.includes(":")) return { value: address, isPrefix: false };

  // Expand "::" so the first four groups can be taken reliably.
  const [head, tail = ""] = address.split("::");
  const headGroups = head ? head.split(":") : [];
  const tailGroups = tail ? tail.split(":") : [];
  const groups = address.includes("::")
    ? [
        ...headGroups,
        ...Array(Math.max(0, 8 - headGroups.length - tailGroups.length)).fill("0"),
        ...tailGroups,
      ]
    : address.split(":");

  if (groups.length < 4) return { value: address, isPrefix: false };
  return { value: `${groups.slice(0, 4).join(":")}::/64`, isPrefix: true };
}

export function NetworkBlockedPage() {
  const dispatch = useDispatch();
  const address = useSelector((s: RootState) => s.auth.networkBlockedAddress) || "";
  const approvable = address ? approvableForm(address) : null;

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

          {approvable && (
            <div className="w-full rounded-xl border border-ink-200 bg-white px-4 py-3 text-left">
              <div className="text-[11px] uppercase tracking-[0.16em] text-ink-500">
                {AUTH_MSG.networkBlockedAddressLabel}
              </div>
              {/* Selectable and monospaced — this is the value they have to read out or paste. */}
              <div className="mt-1 select-all font-mono text-base font-semibold text-ink-900 break-all">
                {approvable.value}
              </div>
              {approvable.isPrefix && (
                <p className="mt-2 text-xs leading-5 text-ink-600">
                  {AUTH_MSG.networkBlockedIpv6Hint}
                  <span className="mt-1 block select-all font-mono text-[11px] text-ink-500 break-all">
                    {address}
                  </span>
                </p>
              )}
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
