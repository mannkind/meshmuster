async function copyText(text: string, button: HTMLElement): Promise<void> {
    const done = () => {
        const original = button.textContent;
        button.textContent = 'copied';
        setTimeout(() => {
            button.textContent = original;
        }, 1200);
    };

    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            done();
            return;
        }
    } catch {
        // Fall through to the textarea.
    }

    const scratch = document.createElement('textarea');
    scratch.value = text;
    scratch.setAttribute('readonly', '');
    scratch.style.position = 'fixed';
    scratch.style.opacity = '0';
    document.body.appendChild(scratch);
    scratch.select();
    try {
        document.execCommand('copy');
        done();
    } finally {
        scratch.remove();
    }
}

document.querySelectorAll<HTMLElement>('.secret[data-secret]').forEach((secret) => {
    const value = secret.dataset.secret ?? '';
    const copy = secret.querySelector<HTMLButtonElement>('.secret-copy');

    copy?.addEventListener('click', () => void copyText(value, copy));
});

function antiforgeryToken(): string {
    const input = document.querySelector<HTMLInputElement>(
        'input[name="__RequestVerificationToken"]',
    );
    return input ? input.value : '';
}
