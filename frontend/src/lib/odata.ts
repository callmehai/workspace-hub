/** OData collection envelope: `{ value, @odata.count? }`. */
export interface ODataResponse<T> {
  value: T[];
  '@odata.count'?: number;
}
