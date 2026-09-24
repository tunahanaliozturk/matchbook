<script setup lang="ts" generic="T">
import type { Column } from "./table";

// Rows of like things with several figures each: the lines of a requisition, an order or an invoice, the entries of a
// ledger. A real <table>, so a screen reader can move by column and announce each header, on the same grouped surface
// as a list. Numeric columns align right and use tabular figures, so amounts line up by their decimal point.
// A cell renders the raw value unless the page provides a slot named after the column: #cell-amount="{ row }".
defineProps<{
    caption: string;
    columns: readonly Column[];
    rows: readonly T[];
    keyOf: (row: T) => string;
}>();

const valueOf = (row: T, key: string): unknown => (row as Record<string, unknown>)[key];
</script>

<template>
    <div class="surface">
        <table class="table">
            <caption class="visually-hidden">
                {{
                    caption
                }}
            </caption>
            <thead>
                <tr>
                    <th
                        v-for="column in columns"
                        :key="column.key"
                        scope="col"
                        :class="{ numeric: column.numeric }"
                    >
                        {{ column.label }}
                    </th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="row in rows" :key="keyOf(row)">
                    <td
                        v-for="column in columns"
                        :key="column.key"
                        :class="{ numeric: column.numeric }"
                    >
                        <slot :name="`cell-${column.key}`" :row="row">{{
                            valueOf(row, column.key)
                        }}</slot>
                    </td>
                </tr>
            </tbody>
            <tfoot v-if="$slots.footer">
                <slot name="footer" />
            </tfoot>
        </table>
    </div>
</template>

<style scoped>
.surface {
    overflow-x: auto;
    background: var(--grouped);
    border-radius: var(--radius-group);
    box-shadow: 0 0 0 0.5px var(--separator);
}

.table {
    width: 100%;
    border-collapse: collapse;
}

th,
td,
.table :slotted(td),
.table :slotted(th) {
    padding: var(--space-2) var(--space-4);
    text-align: left;
    vertical-align: baseline;
}

th {
    font: var(--text-caption);
    font-weight: 600;
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
    white-space: nowrap;
}

tbody tr,
.table :slotted(tr) {
    border-top: 0.5px solid var(--separator);
}

.numeric,
.table :slotted(.numeric) {
    font-variant-numeric: tabular-nums;
    text-align: right;
    white-space: nowrap;
}

.table :slotted(tfoot td),
.table :slotted(td.total) {
    font-weight: 600;
}
</style>
