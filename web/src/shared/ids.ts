// A version 7 UUID for every create the console sends. The services accept a client-chosen id and answer a repeat
// with what the first request made (docs/design.md, "Creates are idempotent"), so a form submitted twice after a
// timeout buys once. Version 7 because lists are ordered by id, and a v7 id sorts by the time it was made.
export function uuidv7(now: number = Date.now()): string {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);

    // 48 bits of Unix milliseconds, big-endian.
    let time = now;
    for (let index = 5; index >= 0; index--) {
        bytes[index] = time % 256;
        time = Math.floor(time / 256);
    }

    bytes[6] = ((bytes[6] ?? 0) & 0x0f) | 0x70; // version 7
    bytes[8] = ((bytes[8] ?? 0) & 0x3f) | 0x80; // RFC 9562 variant

    const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
