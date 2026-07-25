import { Component, type ErrorInfo, type ReactNode } from "react";
import { useLocation } from "react-router-dom";
import { Button, Icon } from "../ui";

interface Props {
  children: ReactNode;
  /** When this value changes the boundary remounts and clears any error. */
  resetKey: string;
}
interface State {
  error: Error | null;
}

/**
 * Content-area error boundary. Unlike the top-level {@link AppErrorBoundary}
 * (which replaces the whole window), this catches a crash inside a single page
 * and shows a compact, calm fallback *inside* the layout — so the sidebar, top
 * bar and navigation stay usable and the user can simply move to another page.
 *
 * It never renders raw error text to the user; details go to the console only.
 */
class RouteErrorBoundaryInner extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // eslint-disable-next-line no-console
    console.error("[RouteErrorBoundary]", error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;

    return (
      <div className="grid place-items-center py-20 px-6">
        <div className="max-w-md text-center">
          <div className="h-12 w-12 rounded-xl bg-rose-50 text-rose-600 grid place-items-center mx-auto mb-4">
            <Icon name="x" size={22} />
          </div>
          <h2 className="text-lg font-semibold text-ink-900">This page ran into a problem</h2>
          <p className="text-sm text-ink-600 mt-2">
            Something on this screen didn&apos;t load correctly. You can reload it,
            or pick another page from the menu — the rest of the app is fine.
          </p>
          <div className="flex items-center justify-center gap-2 mt-6">
            <Button
              onClick={() => this.setState({ error: null })}
              leftIcon={<Icon name="arrowRight" size={14} />}
            >
              Reload this page
            </Button>
          </div>
        </div>
      </div>
    );
  }
}

/**
 * Functional wrapper: keys the boundary on the current pathname so navigating
 * to a different route automatically remounts it and clears a stuck error —
 * no manual reset needed.
 */
export function RouteErrorBoundary({ children }: { children: ReactNode }) {
  const { pathname } = useLocation();
  return <RouteErrorBoundaryInner resetKey={pathname} key={pathname}>{children}</RouteErrorBoundaryInner>;
}
