'use client';

import { useState } from 'react';

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
 * to copy is the `pre` inside the enclosing block of whichever button is pressed.
 */
export function CodeCopyButton() {
  const [copied, setCopied] = useState(false);

  return (
    <button
      type="button"
      className="copy"
      aria-label="Copy code block"
      onClick={(event) => {
        const text = event.currentTarget.closest('.block')?.querySelector('pre')?.textContent ?? '';

        if (text.length === 0) {
          return;
        }

        void copyText(text).then((done) => {
          if (!done) {
            return;
          }

          setCopied(true);
          window.setTimeout(() => {
            setCopied(false);
          }, 1500);
        });
      }}
    >
      {copied ? 'Copied' : 'Copy'}
    </button>
  );
}
