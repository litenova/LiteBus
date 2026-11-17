import { paths } from "./icons.ts";
// Wraps highlighted code blocks in a figure with a header strip that shows the
// language name and a copy button. Plain pre elements without the astro-code
// class, such as diagrams rendered to HTML, pass through untouched.

/** The copy icon drawn from the shared line icon set, hidden from assistive technology. */
function copySvg(): string {
  return (
    `<svg class="icon icon-sm" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" ` +
    `stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths.copy}</svg>`
  );
}

/** Escapes a string for an HTML text node or a double-quoted attribute value. */
function escapeHtml(value: string): string {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

/** The display name of a fence language id. */
export function languageLabel(id: string): string {
  switch (id.toLowerCase()) {
    case "csharp":
    case "c#":
    case "cs":
      return "C#";
    case "bash":
    case "sh":
    case "shell":
    case "shellscript":
    case "zsh":
      return "Shell";
    case "powershell":
    case "pwsh":
      return "PowerShell";
    case "json":
    case "jsonc":
      return "JSON";
    case "ts":
    case "typescript":
      return "TypeScript";
    case "tsx":
      return "TSX";
    case "js":
    case "javascript":
      return "JavaScript";
    case "jsx":
      return "JSX";
    case "html":
      return "HTML";
    case "http":
      return "HTTP";
    case "ini":
      return "INI";
    case "xml":
      return "XML";
    case "css":
      return "CSS";
    case "yaml":
    case "yml":
      return "YAML";
    case "markdown":
    case "md":
      return "Markdown";
    case "sql":
      return "SQL";
    case "plaintext":
    case "text":
      return "Text";
    default:
      return id;
  }
}

/** HTML entities decoded back to the characters the code holds. */
function decodeEntities(value: string): string {
  return value
    .replace(/&#x([0-9a-fA-F]+);/g, (_match, digits: string) =>
      String.fromCodePoint(Number.parseInt(digits, 16)),
    )
    .replace(/&#([0-9]+);/g, (_match, digits: string) =>
      String.fromCodePoint(Number.parseInt(digits, 10)),
    )
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&gt;/g, ">")
    .replace(/&lt;/g, "<")
    .replace(/&amp;/g, "&");
}

/** A highlighted code block: the opening tag, the highlighted lines, and the close. */
const prePattern = /(<pre\b[^>]*class="[^"]*astro-code[^"]*"[^>]*>)([\s\S]*?)(<\/pre>)/g;

/**
 * Wraps each highlighted code block in a figure with a header strip.
 *
 * The strip names the language and carries a copy button whose data-copy holds the block's plain text, so the copy
 * script needs no further parsing.
 */
export function decorateCodeBlocks(html: string): string {
  return html.replace(prePattern, (_match, open: string, inner: string, close: string) => {
    const langMatch = /data-language="([^"]*)"/.exec(open);
    const lang = langMatch ? langMatch[1] : "plaintext";
    const text = decodeEntities(inner.replace(/<[^>]*>/g, ""))
      .replace(/\r\n/g, "\n")
      .replace(/\r/g, "\n")
      .replace(/\n+$/, "");
    const label = escapeHtml(languageLabel(lang));
    const copy = escapeHtml(lang);
    const dataCopy = escapeHtml(text);
    return (
      `<figure class="code-block" data-language="${copy}">` +
      `<figcaption class="code-head" data-pagefind-ignore>` +
      `<span class="code-lang">${label}</span>` +
      `<button type="button" class="btn btn-ghost btn-sm code-copy" data-copy="${dataCopy}" aria-label="Copy code">` +
      `${copySvg()}<span class="copy-label">Copy</span></button></figcaption>` +
      `${open}${inner}${close}</figure>`
    );
  });
}
