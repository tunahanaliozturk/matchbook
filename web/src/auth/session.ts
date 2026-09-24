import { InMemoryWebStorage, UserManager, WebStorageStateStore, type User } from "oidc-client-ts";
import { computed, shallowRef } from "vue";

import { config } from "@/app/config";

import { personFrom, type Person, type Role } from "./roles";

// Authorization code with PKCE against the realm's public client. The tokens live in memory only (ADR 0009): no
// script that runs later can find them in storage, and a reload signs in again through Keycloak's own session,
// which answers at once. Only the sign-in round trip's state and PKCE verifier survive a redirect, in session
// storage, because they have to.
const manager = new UserManager({
    authority: config.authority,
    client_id: config.clientId,
    redirect_uri: `${window.location.origin}/signed-in`,
    post_logout_redirect_uri: `${window.location.origin}/`,
    response_type: "code",
    scope: "openid profile",
    userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    // Renews with the refresh token before the fifteen-minute access token runs out, for as long as the tab is open.
    automaticSilentRenew: true,
    monitorSession: false,
});

const user = shallowRef<User | null>(null);

manager.events.addUserLoaded((loaded) => {
    user.value = loaded;
});
manager.events.addUserUnloaded(() => {
    user.value = null;
});
// A refresh that fails means the Keycloak session ended. Forget the user; the next navigation signs in again.
manager.events.addSilentRenewError(() => {
    user.value = null;
});

const person = computed<Person | null>(() =>
    user.value ? personFrom(user.value.access_token) : null,
);

export function useSession() {
    return {
        person,
        signedIn: computed(() => person.value !== null),
        has: (role: Role) => person.value?.roles.has(role) ?? false,
        hasAny: (wanted: readonly Role[]) =>
            wanted.some((role) => person.value?.roles.has(role) ?? false),
    };
}

/** The bearer token for the next request, or nothing when signed out. */
export function accessToken(): string | undefined {
    return user.value && !user.value.expired ? user.value.access_token : undefined;
}

/** Sends the browser to Keycloak, to come back to <paramref name="returnTo"/>. */
export async function signIn(returnTo: string): Promise<void> {
    await manager.signinRedirect({ state: returnTo });
}

/** Finishes the round trip on the redirect page and says where the person was going. */
export async function completeSignIn(): Promise<string> {
    const signedIn = await manager.signinRedirectCallback();
    user.value = signedIn;
    return typeof signedIn.state === "string" && signedIn.state.startsWith("/")
        ? signedIn.state
        : "/";
}

export async function signOut(): Promise<void> {
    const idToken = user.value?.id_token;
    user.value = null;
    await manager.signoutRedirect(idToken ? { id_token_hint: idToken } : {});
}
