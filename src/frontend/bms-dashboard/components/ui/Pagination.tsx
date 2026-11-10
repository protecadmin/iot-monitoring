"use client";

type PaginationProps = {
  page: number;
  pageSize: number;
  totalCount: number;
  itemLabel: string;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
  pageSizes?: number[];
};

export function Pagination({ page, pageSize, totalCount, itemLabel, onPageChange, onPageSizeChange, pageSizes = [10, 25, 50] }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-xs text-muted-foreground">
      <div className="flex items-center gap-3">
        <span>{totalCount === 0 ? `0 ${itemLabel}` : `${first}–${last} of ${totalCount} ${itemLabel}`}</span>
        {onPageSizeChange && <label className="flex items-center gap-1.5"><span>Rows</span><select value={pageSize} onChange={(event) => onPageSizeChange(Number(event.target.value))} className="rounded border border-input bg-background px-1.5 py-1 text-xs text-foreground" aria-label={`Rows per page for ${itemLabel}`}>{pageSizes.map((size) => <option key={size} value={size}>{size}</option>)}</select></label>}
      </div>
      <div className="flex items-center gap-2"><span>Page {page} of {totalPages}</span><button type="button" disabled={page <= 1} onClick={() => onPageChange(page - 1)} className="rounded border border-input px-2.5 py-1 text-foreground disabled:cursor-not-allowed disabled:opacity-40">Previous</button><button type="button" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)} className="rounded border border-input px-2.5 py-1 text-foreground disabled:cursor-not-allowed disabled:opacity-40">Next</button></div>
    </div>
  );
}
