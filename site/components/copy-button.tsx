'use client';

import { useEffect, useRef, useState } from 'react';

/**
 * The last confirmation message announced from any copy button on the page.
 *
 * VoiceOver skips a message identical to the previous one from the same region, so a repeated
 * message gets a trailing no-break space that is never read aloud.
 */
let lastMessage = '';

/**
 * Copies the install command to the clipboard.
 *
 * Renders the mock `.copy` button used in `.install` rows with a `role="status"` bubble beside
 * it. The bubble is both the visible feedback and the live region: it shows `Copied` after a
 * successful write and `Copy failed` when the write is rejected, and clears after two seconds.
 * A repeat click restarts the feedback instead of stacking timers, and the button is never
 * disabled so a failed copy can be retried.
 */
export function CopyButton({ text, label }: Readonly<{ text: string; label?: string }>) {
  const [copied, setCopied] = useState(false);
  const [failed, setFailed] = useState(false);
  const tipRef = useRef<HTMLSpanElement>(null);
  const timerRef = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current);
      }
    };
  }, []);

  /** Shows the outcome in the bubble and clears it after two seconds. */
  function report(ok: boolean): void {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
    }
    setCopied(false);
    setFailed(false);
    let message = ok ? 'Copied' : 'Copy failed';
    // A repeat gets a trailing no-break space (silent) so VoiceOver announces it again.
    if (message === lastMessage) {
      message += ' ';
    }
    lastMessage = message;
    const tip = tipRef.current;
    if (tip !== null) {
      tip.textContent = '';
    }
    if (ok) {
      setCopied(true);
    } else {
      setFailed(true);
    }
    requestAnimationFrame(() => {
      if (tipRef.current !== null) {
        tipRef.current.textContent = message;
      }
    });
    timerRef.current = window.setTimeout(() => {
      timerRef.current = null;
      setCopied(false);
      setFailed(false);
      if (tipRef.current !== null) {
        tipRef.current.textContent = '';
      }
    }, 2000);
  }

  return (
    <span className="copy-wrap">
      <button
        type="button"
        className="copy"
        aria-label={label ?? 'Copy install command'}
        data-copied={copied || undefined}
        data-failed={failed || undefined}
        onClick={() => {
          navigator.clipboard.writeText(text).then(
            () => report(true),
            () => report(false),
          );
        }}
      >
        Copy
      </button>
      <span ref={tipRef} className="copy-tip" role="status" />
    </span>
  );
}
