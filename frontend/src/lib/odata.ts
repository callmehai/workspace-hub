/** OData collection envelope: `{ value, @odata.count? }`. */
export interface ODataResponse<T> {
  value: T[];
  '@odata.count'?: number;
}

/** Escape single quotes trong OData string literal. */
export function odataEscape(value: string): string {
  return value.replace(/'/g, "''");
}

export function parseODataResponse<T>(data: unknown): ODataResponse<T> {
  if (data && typeof data === 'object' && Array.isArray((data as ODataResponse<T>).value)) {
    return data as ODataResponse<T>;
  }
  if (Array.isArray(data)) {
    return { value: data as T[], '@odata.count': data.length };
  }
  return { value: [], '@odata.count': 0 };
}
