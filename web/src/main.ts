import "./styles/tokens.css";
import "./styles/base.css";
import "./shared/api/clients";

import { MutationCache, QueryCache, QueryClient, VueQueryPlugin } from "@tanstack/vue-query";
import { createApp } from "vue";

import App from "./App.vue";
import { router } from "./app/router";
import { signIn } from "./auth/session";
import { ApiProblem, isTransient } from "./shared/api/problem";

// A 401 anywhere means the session ended under the page: sign in again and come back to the same place.
const onError = (error: unknown) => {
    if (error instanceof ApiProblem && error.status === 401) {
        void signIn(router.currentRoute.value.fullPath);
    }
};

const queries = new QueryClient({
    queryCache: new QueryCache({ onError }),
    mutationCache: new MutationCache({ onError }),
    defaultOptions: {
        queries: {
            // Another person's action changes what this one sees (an approval, a receipt), so data goes stale quickly
            // and is fetched again when the window regains focus.
            staleTime: 10_000,
            retry: (failures, error) => isTransient(error) && failures < 2,
        },
        mutations: { retry: false },
    },
});

createApp(App).use(router).use(VueQueryPlugin, { queryClient: queries }).mount("#app");
