import { describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
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
    soldAt: "2026-10-01T00:00:00Z", validatedAt: null,
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
