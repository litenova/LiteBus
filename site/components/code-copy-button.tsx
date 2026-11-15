'use client';

import { useRef, useState } from 'react';

/**
 * The last confirmation message announced on the page.
 *
 * VoiceOver skips a message identical to the previous one from the same region, so a repeated
 * message gains a no-break space to differ. One page-wide value covers every copy button.
 */
let lastMessage = '';

/**
 * Copies text to the clipboard, reporting whether it worked.
 */
async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);

    return true;
  } catch {
    return false;
  }
}

/**
 * Copies the code block it sits above.
 *
 * The docs renderer wraps every fenced code block in `div.block` with this button in the block head, so the block
 * to copy is the `pre` inside the enclosing block of whichever button is pressed. Success and failure both show in
 * the button label and in a live region outside the button so sighted and screen reader users get the same message.
 */
export function CodeCopyButton() {
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle');
  const [announcement, setAnnouncement] = useState('');
  const timerRef = useRef<number | undefined>(undefined);

  const report = (ok: boolean) => {
    window.clearTimeout(timerRef.current);

    let message = ok ? 'Copied' : 'Copy failed';

    if (message === lastMessage) {
      message += ' ';
    }

    lastMessage = message;
    setStatus(ok ? 'copied' : 'failed');
    setAnnouncement('');

    requestAnimationFrame(() => {
      setAnnouncement(message);
    });

    timerRef.current = window.setTimeout(() => {
      setStatus('idle');
      setAnnouncement('');
    }, 2000);
  };

  return (
    <>
      <button
        type="button"
        className="copy"
        aria-label={status === 'copied' ? 'Copied' : status === 'failed' ? 'Copy failed' : 'Copy code block'}
        onClick={(event) => {
          const target = event.currentTarget.closest('.block')?.querySelector('pre');
          const text = target?.innerText.trim() ?? '';

          if (text.length === 0) {
            return;
          }

          void copyText(text).then(report);
        }}
      >
        {status === 'copied' ? 'Copied' : status === 'failed' ? 'Failed' : 'Copy'}
      </button>
      <span className="sr-only" role="status">
        {announcement}
      </span>
    </>
  );
}
