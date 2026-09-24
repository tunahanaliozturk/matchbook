<script setup lang="ts">
import EmptyState from "@/shared/ui/EmptyState.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
</script>

<template>
    <PageHeader title="Not found" />
    <EmptyState
        message="Nothing lives at this address. It may have been mistyped, or the link may be out of date."
    >
        <RouterLink :to="{ name: 'inbox' }">Go to your inbox</RouterLink>
    </EmptyState>
</template>
