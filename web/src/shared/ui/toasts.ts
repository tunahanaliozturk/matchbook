import { shallowRef } from "vue";

export interface Toast {
    id: number;
    message: string;
}

// Confirmations of what just happened ("Requisition submitted"), shown for a few seconds in a polite live region.
// Errors are not toasts: they stay on the page next to what failed, until they are dealt with.
const toasts = shallowRef<readonly Toast[]>([]);
const lifetime = 4000;
let next = 1;

function dismiss(id: number) {
    toasts.value = toasts.value.filter((toast) => toast.id !== id);
}

export function useToasts() {
    return {
        toasts,
        dismiss,
        confirm(message: string) {
            const id = next++;
            toasts.value = [...toasts.value, { id, message }];
            setTimeout(() => dismiss(id), lifetime);
        },
    };
}
