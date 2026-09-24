/** The name the server gave a file in its Content-Disposition header, or the fallback when it gave none. */
export function fileNameOf(disposition: string | null, fallback: string): string {
    const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? "")?.[1];
    const plain = /filename="?([^";]+)"?/i.exec(disposition ?? "")?.[1];
    return encoded ? decodeURIComponent(encoded) : (plain ?? fallback);
}

/** Hands a file already in memory to the browser as a download, the way a link with a download attribute would. */
export function saveFile(file: Blob, name: string): void {
    const url = URL.createObjectURL(file);
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    // Revoked once the click has been handled, or some browsers cancel the download they were starting.
    setTimeout(() => URL.revokeObjectURL(url), 0);
}
