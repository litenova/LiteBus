'use client';

import { useState } from 'react';

/**
 * Copies an install command to the clipboard.
 *
 * Renders the copy button used in `.cmd` rows. After a successful write the
 * label flips to `Copied` for 1500 ms, matching the static reference site.
 */
export function CopyButton({ text, label }: Readonly<{ text: string; label?: string }>) {
  const [copied, setCopied] = useState(false);

  return (
    <button
      type="button"
      className="copy-cmd"
      data-copy={text}
      aria-label={label ?? `Copy ${text}`}
      onClick={() => {
        if (!navigator.clipboard) {
          return;
        }

        navigator.clipboard.writeText(text).then(
          () => {
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          },
          () => {},
        );
      }}
    >
      {copied ? 'Copied' : 'Copy'}
    </button>
  );
}
