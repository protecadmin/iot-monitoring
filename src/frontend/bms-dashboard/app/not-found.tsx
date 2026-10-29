import { ErrorView } from "@/components/ui/ErrorView";

/** Any address that matches no page. */
export default function NotFound() {
  return <ErrorView kind="not-found" />;
}
