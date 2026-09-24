/** A column of a UiTable. Numeric columns align right and use tabular figures. */
export interface Column {
    key: string;
    label: string;
    numeric?: boolean;
}
