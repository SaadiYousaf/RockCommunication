import { Component, type ErrorInfo, type ReactNode } from "react";
import { Button, Card, CardBody, Icon } from "../ui";

interface State {
  error: Error | null;
}

/**
 * Top-level error boundary. Catches render errors from anywhere in the tree
 * and replaces them with a calm, branded "something went wrong" panel + a
 * Try-again button — instead of React's default white-of-death.
 *
 * Errors are logged to the console so a dev can still see what blew up.
 */
export class AppErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // eslint-disable-next-line no-console
    console.error("[AppErrorBoundary]", error, info.componentStack);
  }

  reset = () => this.setState({ error: null });

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    return (
      <div className="min-h-screen grid place-items-center p-6 bg-ink-50">
        <Card className="max-w-lg w-full" elevated>
          <CardBody>
            <div className="h-12 w-12 rounded-xl bg-rose-50 text-rose-600 grid place-items-center mb-4">
              <Icon name="x" size={22} />
            </div>
            <h2 className="text-xl font-semibold text-ink-900">Something went wrong</h2>
            <p className="text-sm text-ink-600 mt-2">
              The page hit an unexpected error. Your session is still active —
              you can try again, or jump back to the dashboard.
            </p>
            <details className="mt-4 text-xs text-ink-500">
              <summary className="cursor-pointer select-none hover:text-ink-700">
                Technical details
              </summary>
              <pre className="mt-2 p-3 bg-ink-50 rounded-lg overflow-auto max-h-48 whitespace-pre-wrap break-words text-[11px] font-mono text-ink-700">
                {error.message}
                {error.stack ? `\n\n${error.stack}` : ""}
              </pre>
            </details>
            <div className="flex gap-2 mt-6">
              <Button onClick={this.reset} leftIcon={<Icon name="arrowRight" size={14} />}>
                Try again
              </Button>
              <Button
                variant="outline"
                onClick={() => { this.reset(); window.location.assign("/dashboard"); }}
              >
                Back to dashboard
              </Button>
            </div>
          </CardBody>
        </Card>
      </div>
    );
  }
}
