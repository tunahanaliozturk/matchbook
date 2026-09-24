<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";

import { completeSignIn } from "@/auth/session";
import InlineNotice from "@/shared/ui/InlineNotice.vue";

// Where Keycloak sends the browser back. Exchanges the code for tokens, then continues to the page the person
// was going to.
const router = useRouter();
const failed = ref(false);

onMounted(async () => {
    try {
        await router.replace(await completeSignIn());
    } catch {
        failed.value = true;
    }
});
</script>

<template>
    <main class="signing-in">
        <h1 tabindex="-1" class="visually-hidden">Signing in</h1>
        <InlineNotice v-if="failed" tone="error">
            Signing in did not complete. <a href="/">Start again</a>.
        </InlineNotice>
        <p v-else class="wait" role="status">Signing in…</p>
    </main>
</template>

<style scoped>
.signing-in {
    display: grid;
    place-items: center;
    min-height: 100vh;
    padding: var(--space-6);
}

.wait {
    color: var(--label-secondary);
}
</style>
