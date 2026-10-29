"use client";

import { useEffect } from "react";
import { ErrorView } from "@/components/ui/ErrorView";

/** A page inside the app crashed: shown in place of the page, with the sidebar still there. */
export default function ProtectedError({ error, retry }: { error: Error & { digest?: string }; retry: () => void }) {
  useEffect(() => {
    console.error(error);
  }, [error]);
  return <ErrorView kind="error" digest={error.digest} onRetry={retry} />;
}
