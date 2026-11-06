import { describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { anAuthState, aUser, renderWithProviders } from "../../test/renderWithProviders";
import type { ValidatorQueueItem } from "../../shared/api/types";

/**
 * A submitted sale. Defaults are a plain one; each test overrides the few fields it is about.
 *
 * The closer's figures and the carrier's approved figures are deliberately far apart, so a test
 * asserting on "Globe Life" or "$61.00" cannot pass by coincidence.
 */
function aSale(overrides: Partial<ValidatorQueueItem> = {}): ValidatorQueueItem {
  return {
    saleId: "s1", leadId: "l1",
    agencyId: "a1", agencyName: "SMH",
    callCenterId: "cc1", callCenterName: "MAK BPO",
    leadName: "Rosa Tester", leadPhone: "5551234567", state: "AZ",
    carrier: "Mutual of Omaha",
    policyNumber: "POL-1",
    monthlyPremium: 40,
    closerUserId: "c1", closerName: "Laraib",
    status: "Completed",
    carrierApproved: null, coverageApproved: null, premiumApproved: null, planApproved: null,
    declineReason: null, submissionComment: null,
    validatorUserId: null, validatorName: null,
    licenseAgentUserId: null, licenseAgentName: null,
    licenseAgentAgencyId: null, licenseAgentAgencyName: null,
    // The CURRENT month, because the premium card reports on the selected month and the page
    // opens on today's. A hard-coded date silently zeroed the card the moment the month rolled.
    soldAt: new Date(new Date().getFullYear(), new Date().getMonth(), 1, 12).toISOString(),
    validatedAt: null,
    initialDraftDate: null,
    ...overrides,
  };
}

let rows: ValidatorQueueItem[] = [];

// Only the queue read is stubbed; everything else in baseApi stays real, so the page renders the
// way it does in the app rather than against a hand-built imitation of the api module.
vi.mock("../../shared/api/baseApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../shared/api/baseApi")>();
  return {
    ...actual,
    useValidatorQueueQuery: () => ({ data: rows, isLoading: false }),
  };
});

const { ValidateQueuePage } = await import("./ValidateQueuePage");

function render(sales: ValidatorQueueItem[]) {
  rows = sales;
  return renderWithProviders(<ValidateQueuePage />, {
    auth: anAuthState({ user: aUser({ roles: ["Validator"] }) }),
  });
}

/**
 * Assertions about a COLUMN are scoped to the table.
 *
 * The same premium also shows on the "Premium / mo" card above it, so an unscoped getByText finds
 * two matches and fails for a reason that has nothing to do with the column.
 */
const table = () => within(screen.getByRole("table"));

describe("the submission queue's carrier and premium", () => {
  /**
   * Before a submission agent has done anything, the closer's figures are all there is.
   */
  it("shows what the closer submitted until the carrier has approved something", () => {
    render([aSale()]);

    expect(table().getByText("Mutual of Omaha")).toBeInTheDocument();
    expect(table().getByText("$40.00")).toBeInTheDocument();
  });

  /**
   * THE rule: once approved, the approved figures are the only ones on screen.
   *
   * The closer writes down what they sold and the carrier decides what it actually issued — a
   * different carrier, a loaded premium. Leaving the closer's figures up meant the column was
   * stale the moment the work was done, and the premium a submission agent read all day was not
   * the one the carrier would draft.
   */
  it("shows only the approved carrier and premium once they are recorded", () => {
    render([aSale({
      status: "Approved",
      carrierApproved: "Globe Life",
      premiumApproved: 61,
    })]);

    expect(table().getByText("Globe Life")).toBeInTheDocument();
    expect(table().getByText("$61.00")).toBeInTheDocument();

    // And the closer’s originals are gone from the table — "only" is the whole instruction.
    expect(table().queryByText("Mutual of Omaha")).not.toBeInTheDocument();
    expect(table().queryByText("$40.00")).not.toBeInTheDocument();
  });

  /**
   * A carrier approved without a premium change, and vice versa. Each column follows its own
   * field, so one being filled must not drag the other along.
   */
  it("switches each column independently", () => {
    render([aSale({ carrierApproved: "Globe Life" })]);

    expect(table().getByText("Globe Life")).toBeInTheDocument();
    expect(table().getByText("$40.00")).toBeInTheDocument();      // premium untouched
  });

  /**
   * An approved premium of zero is a real figure, not a missing one. `||` would quietly fall back
   * to the closer's number and show money that the carrier is not going to draft.
   */
  it("treats an approved premium of zero as an answer", () => {
    render([aSale({ status: "Approved", premiumApproved: 0 })]);

    expect(table().getByText("$0.00")).toBeInTheDocument();
    expect(table().queryByText("$40.00")).not.toBeInTheDocument();
  });

  /**
   * An empty approved carrier is NOT an answer — blanking the field must not blank the column.
   */
  it("ignores a blank approved carrier", () => {
    render([aSale({ carrierApproved: "   " })]);

    expect(table().getByText("Mutual of Omaha")).toBeInTheDocument();
  });


  /**
   * The month governs the PREMIUM card, not the list.
   *
   * The queue used to fetch only the selected month, which turned a work queue into a monthly
   * report: a sale submitted in September and still untouched vanished on 1 October. Now the list
   * carries everything outstanding while the money figure reports on the month being reconciled.
   */
  it("keeps an older sale in the list but out of the month's premium", () => {
    const lastMonth = new Date();
    lastMonth.setMonth(lastMonth.getMonth() - 1);

    render([
      aSale({ saleId: "s1", leadName: "Thismonth Customer", status: "Approved", premiumApproved: 61 }),
      aSale({
        saleId: "s2", leadName: "Lastmonth Customer", status: "Approved", premiumApproved: 999,
        soldAt: new Date(lastMonth.getFullYear(), lastMonth.getMonth(), 15, 12).toISOString(),
      }),
    ]);

    // Still workable — it is outstanding, whatever month it came from.
    expect(table().getByText("Lastmonth Customer")).toBeInTheDocument();

    // But it is not this month's money: the card must not have added the 999 to the 61.
    // Asserting the absence of the COMBINED figure, because $61.00 itself appears twice — once
    // in the card and once in its own row.
    expect(screen.queryByText("$1,060.00")).not.toBeInTheDocument();
  });

  /**
   * The premium card totals approved sales using the same approved figure. If the card and the
   * column disagreed, one of the two numbers on the screen would always be wrong.
   */
  it("totals the approved premium on the premium card", () => {
    render([
      aSale({ saleId: "s1", status: "Approved", premiumApproved: 61 }),
      aSale({ saleId: "s2", status: "Approved", premiumApproved: 39 }),
      // Not approved, so it contributes nothing however large it is.
      aSale({ saleId: "s3", status: "Decline", monthlyPremium: 500 }),
    ]);

    expect(screen.getByText("$100.00")).toBeInTheDocument();
  });
});

/**
 * The status buckets, as the owner defined them on 6 Nov 2026.
 *
 * They are deliberately not all scoped the same way, which is exactly why these tests exist: the
 * difference is invisible in the code and reads as a bug on screen unless it is held in place.
 *
 *   All                      every sale ever — a duplicate is checked against it by hand
 *   Completed                the chosen month only; a working list, not a history
 *   Approved                 all time, first draft date still to come
 *   Pending initial payment  all time, the draft date has passed with nothing collected
 */
describe("the submission queue's status buckets", () => {
  const lastMonth = () => {
    const d = new Date();
    d.setMonth(d.getMonth() - 1);
    return new Date(d.getFullYear(), d.getMonth(), 15, 12).toISOString();
  };

  /**
   * The chip carrying a status, found by its label rather than its position in the row.
   *
   * By text content, not accessible name: the label and the count sit in adjacent spans with no
   * whitespace between them, so the computed name is "Completed1".
   */
  const chip = (label: string) => {
    const found = screen.getAllByRole("button")
      .find((b) => (b.textContent ?? "").trim().startsWith(label));
    if (!found) throw new Error(`No status chip labelled "${label}"`);
    return found;
  };

  /**
   * Completed follows the month. An untouched Completed sale from four months ago is history, and
   * months of them pile up in front of the one bucket somebody is meant to clear each month.
   */
  it("leaves an older Completed sale out of the Completed bucket", async () => {
    const user = userEvent.setup();
    render([
      aSale({ saleId: "s1", leadName: "Thismonth Customer", status: "Completed" }),
      aSale({ saleId: "s2", leadName: "Lastmonth Customer", status: "Completed", soldAt: lastMonth() }),
    ]);

    // Both are in All — that is the whole record, and it is what a manual duplicate check reads.
    expect(table().getByText("Lastmonth Customer")).toBeInTheDocument();

    await user.click(chip("Completed"));

    expect(table().getByText("Thismonth Customer")).toBeInTheDocument();
    expect(table().queryByText("Lastmonth Customer")).not.toBeInTheDocument();
  });

  /**
   * Approved is a live financial state, so it does NOT stop at the month boundary. A sale approved
   * in October whose draft is still ahead of it is money this floor is still expecting.
   */
  it("keeps an older Approved sale in the Approved bucket", async () => {
    const user = userEvent.setup();
    render([
      aSale({ saleId: "s1", leadName: "Thismonth Customer", status: "Approved" }),
      aSale({ saleId: "s2", leadName: "Lastmonth Customer", status: "Approved", soldAt: lastMonth() }),
    ]);

    await user.click(chip("Approved"));

    expect(table().getByText("Thismonth Customer")).toBeInTheDocument();
    expect(table().getByText("Lastmonth Customer")).toBeInTheDocument();
  });

  /**
   * Same for Pending initial payment: a draft that failed in October is still unpaid in November.
   */
  it("keeps an older Pending initial payment sale in its bucket", async () => {
    const user = userEvent.setup();
    render([
      aSale({ saleId: "s2", leadName: "Lastmonth Customer", status: "PendingInitialPayment", soldAt: lastMonth() }),
    ]);

    await user.click(chip("Pending initial payment"));

    expect(table().getByText("Lastmonth Customer")).toBeInTheDocument();
  });

  /**
   * The All chip counts the WHOLE queue, including the Completed sales its own bucket holds back.
   * Scoping the rows behind the chips without saying so made All under-report the record that a
   * duplicate check depends on being complete.
   */
  it("counts every sale on the All chip, including out-of-month Completed ones", () => {
    render([
      aSale({ saleId: "s1", status: "Completed" }),
      aSale({ saleId: "s2", status: "Completed", soldAt: lastMonth() }),
      aSale({ saleId: "s3", status: "Approved" }),
    ]);

    expect(chip("All")).toHaveTextContent("3");
    // While Completed itself reports only what it will actually show.
    expect(chip("Completed")).toHaveTextContent("1");
  });

  /**
   * The draft date is what decides between the two buckets, so it belongs on the row. Without it
   * a sale appears to have moved itself for no stated reason.
   */
  it("shows the draft date on the statuses it decides", () => {
    render([
      aSale({ saleId: "s1", status: "Approved", initialDraftDate: "2026-12-01T00:00:00Z" }),
    ]);

    expect(table().getByText(/First draft due/)).toBeInTheDocument();
  });

  it("says the draft date has passed on a pending-payment sale", () => {
    render([
      aSale({ saleId: "s1", status: "PendingInitialPayment", initialDraftDate: "2026-10-01T00:00:00Z" }),
    ]);

    expect(table().getByText(/First draft was due/)).toBeInTheDocument();
  });

  /** A status the date does not govern must not grow a draft line. */
  it("does not show a draft date on an unrelated status", () => {
    render([
      aSale({ saleId: "s1", status: "Decline", initialDraftDate: "2026-10-01T00:00:00Z" }),
    ]);

    expect(table().queryByText(/First draft/)).not.toBeInTheDocument();
  });
});
