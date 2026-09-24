<script setup lang="ts">
import { computed } from "vue";

import { useSession } from "@/auth/session";
import EmptyState from "@/shared/ui/EmptyState.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";

import { queues } from "./queues";

const { hasAny } = useSession();

const mine = computed(() => queues.filter((queue) => hasAny(queue.roles)));
</script>

<template>
    <PageHeader title="Inbox" />
    <EmptyState
        v-if="mine.length === 0"
        message="Nothing here waits on your roles. Everything you can see is in the list on the left."
    />
    <component :is="queue.component" v-for="queue in mine" :key="queue.key" />
</template>
