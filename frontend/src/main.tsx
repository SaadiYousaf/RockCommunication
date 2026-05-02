import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Provider } from "react-redux";
import "./index.css";
import { AppRouter } from "./app/router";
import { store } from "./app/store";
import { ToastProvider } from "./shared/ui";
import { AppErrorBoundary } from "./shared/components/AppErrorBoundary";
import { ConfirmProvider } from "./shared/components/ConfirmDialog";
import { TopProgressBar } from "./shared/components/TopProgressBar";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AppErrorBoundary>
      <Provider store={store}>
        <ToastProvider>
          <ConfirmProvider>
            <TopProgressBar />
            <AppRouter />
          </ConfirmProvider>
        </ToastProvider>
      </Provider>
    </AppErrorBoundary>
  </StrictMode>,
);
