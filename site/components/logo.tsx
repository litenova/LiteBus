/**
 * LiteBus logo mark, drawn inline from the paths in `site/public/icon.svg`.
 *
 * The outline strokes `var(--logo-line)` at width 9 in the 128 view box with round caps and
 * joins; the route fills `var(--logo-route)`. Both follow the active theme through CSS.
 */
export function Logo({ size = 20 }: Readonly<{ size?: number }>) {
  return (
    <svg
      className="logo"
      viewBox="0 0 128 128"
      width={size}
      height={size}
      style={{ width: size, height: size }}
      aria-hidden="true"
    >
      <path className="o" d="M84 4 36 53l24 23-16 48 48-49-25-21L84 4Z" />
      <path className="r" d="m68 36-15 16-2 3-1 2 1 4 14 14-11 34 34-34-13-10 2-4-4-5-9-2 4-18Z" />
    </svg>
  );
}

/**
 * GitHub octicon mark, using the path from the previous site header.
 *
 * Sized by the `.ghm` class in `globals.css` (16px in the header, 12px in the status bar).
 */
export function GitHubMark() {
  return (
    <svg className="ghm" viewBox="0 0 16 16" aria-hidden="true" fill="currentColor">
      <path d="M8 0c4.42 0 8 3.58 8 8a8.013 8.013 0 0 1-5.45 7.59c-.4.08-.55-.17-.55-.38 0-.27.01-1.13.01-2.2 0-.75-.25-1.23-.54-1.48 1.78-.2 3.65-.88 3.65-3.95 0-.88-.31-1.59-.82-2.15.08-.2.36-1.02-.08-2.12 0 0-.67-.22-2.2.82-.64-.18-1.32-.27-2-.27-.68 0-1.36.09-2 .27-1.53-1.03-2.2-.82-2.2-.82-.44 1.1-.16 1.92-.08 2.12-.51.56-.82 1.28-.82 2.15 0 3.06 1.86 3.75 3.64 3.95-.23.2-.44.55-.51 1.07-.46.21-1.61.55-2.33-.66-.15-.24-.6-.83-1.23-.82-.67.01-.27.38.01.53.34.19.73.9.82 1.13.16.45.68 1.31 2.69.94 0 .67.01 1.3.01 1.49 0 .21-.15.45-.55.38A7.995 7.995 0 0 1 0 8c0-4.42 3.58-8 8-8Z" />
    </svg>
  );
}
