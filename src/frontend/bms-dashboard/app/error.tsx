"use client";

import { useEffect } from "react";
import { ErrorView } from "@/components/ui/ErrorView";

/** A crash outside the signed-in pages (e.g. login) or in the app shell itself. */
export default function RootError({ error, retry }: { error: Error & { digest?: string }; retry: () => void }) {
  useEffect(() => {
    console.error(error);
  }, [error]);
  return <ErrorView kind="error" digest={error.digest} onRetry={retry} />;
}
