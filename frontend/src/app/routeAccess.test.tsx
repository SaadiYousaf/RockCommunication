import { describe, expect, it } from "vitest";
import { isValidElement, type ReactElement } from "react";
import type { RouteObject } from "react-router-dom";
import { appRoutes } from "./router";
import { NAV, type NavNode } from "../shared/constants/nav";
import { ProtectedRoute } from "../shared/components/ProtectedRoute";

/**
 * The sidebar and the route table have to agree about who may reach a page.
 *
 * They disagreed once and it cost three days. "Add Lead" was offered to Closers in the sidebar while
 * its route allowed only Fronters, so every Closer who clicked it filled in the whole intake form
 * and was refused at the end. The API was right the entire time, which is exactly why it took so
 * long to find — nothing on the server could have caught it, and neither could a test of the guard
 * in isolation, because the guard was doing precisely what the route told it to.
 *
 * This walks the real route table against the real sidebar definition. It needs no fixtures and
 * no mocks: if someone adds a nav entry for a role the route refuses, this fails.
 */

interface GuardedRoute {
  path: string;
  roles?: string[];
  modules?: string[];
}

/** Flattens the route tree, carrying each ProtectedRoute's role/module gate down to its children. */
function collectGuardedRoutes(
  routes: readonly RouteObject[],
  inherited: { roles?: string[]; modules?: string[] } = {},
): GuardedRoute[] {
  const out: GuardedRoute[] = [];

  for (const route of routes) {
    let gate = inherited;

    // A layout route whose element IS the guard contributes its gate to everything beneath it.
    const el = route.element;
    if (isValidElement(el) && (el as ReactElement).type === ProtectedRoute) {
      const props = (el as ReactElement<{ roles?: string[]; modules?: string[] }>).props;
      // The innermost guard wins where both name roles; otherwise they combine.
      gate = {
        roles: props.roles ?? inherited.roles,
        modules: props.modules ?? inherited.modules,
      };
    }

    if (route.path) out.push({ path: route.path, ...gate });
    if (route.children) out.push(...collectGuardedRoutes(route.children, gate));
  }
  return out;
}

/** Every nav leaf that points somewhere and names the roles allowed to see it. */
function collectNavLeaves(nodes: readonly NavNode[]): { to: string; roles: string[]; label: string }[] {
  const out: { to: string; roles: string[]; label: string }[] = [];
  for (const n of nodes) {
    if (n.to && n.roles) out.push({ to: n.to, roles: n.roles, label: n.label ?? n.to });
    if (n.children) out.push(...collectNavLeaves(n.children));
  }
  return out;
}

const guarded = collectGuardedRoutes(appRoutes);
const navLeaves = collectNavLeaves(NAV);

describe("the sidebar and the route table agree", () => {
  it("finds the route table and the sidebar", () => {
    expect(guarded.length).toBeGreaterThan(20);
    expect(navLeaves.length).toBeGreaterThan(0);
  });

  it.each(navLeaves)("$label is reachable by every role it is offered to", ({ to, roles, label }) => {
    const route = guarded.find((r) => r.path === to);
    expect(route, `no route is declared for the "${label}" sidebar entry (${to})`).toBeDefined();

    // No role gate on the route means anyone signed in may enter — nothing to disagree about.
    if (!route!.roles) return;

    const refused = roles.filter((r) => !route!.roles!.includes(r));
    expect(
      refused,
      `the sidebar offers "${label}" (${to}) to ${refused.join(", ")}, but its route only admits ` +
      `${route!.roles!.join(", ")} — they would click it and be shown a 403`,
    ).toEqual([]);
  });

  it("points every sidebar entry at a route that exists", () => {
    const paths = new Set(guarded.map((r) => r.path));
    const dangling = navLeaves.filter((n) => !paths.has(n.to)).map((n) => `${n.label} → ${n.to}`);
    expect(dangling, "these sidebar entries lead nowhere").toEqual([]);
  });
});
