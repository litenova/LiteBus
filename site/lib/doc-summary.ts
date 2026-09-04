/**
 * Title and description derived from a page's Markdown.
 *
 * The documentation sources carry no frontmatter. Every page opens with an H1, and what follows it is one of three
 * shapes: an intro paragraph written for a reader, a metadata block with a `Summary` field, or a metadata block
 * followed by a `## Summary` section. Deriving the description from whichever shape a page uses keeps one copy of the
 * wording, so a page that is rewritten does not also have to have its frontmatter remembered.
 *
 * The description feeds the page's meta and Open Graph tags, the search index, and the note beside each entry in
 * `llms.txt`.
 */

/** Strips a byte-order mark, which the content root has at least one of. */
function withoutByteOrderMark(content: string): string {
  return content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;
}

/** The text of the first level-one heading. */
export function extractTitle(content: string): string | undefined {
  return /^#\s+(.+?)\s*$/m.exec(withoutByteOrderMark(content))?.[1];
}

/** Lines that open a block which is not prose, and so cannot contribute to a description. */
const nonProsePattern = /^(?:#{1,6}\s|```|~~~|\||>|[-*+]\s|\d+[.)]\s|<|\[!|:::)/;

/**
 * A metadata line, which the catalog and roadmap pages use to state a capability's identifier and maturity.
 *
 * Written either as a list item (`- **Summary**: text`) or as a bare line (`**Maturity:** GA`). The label is capped in
 * length so that an ordinary sentence opening with a bold phrase is not mistaken for a field.
 */
const metadataLinePattern = /^-?\s*\*\*([A-Za-z][^*]{0,48}?)\*\*\s*:?\s*(.*)$/;

/** The heading whose section holds the description on a page that opens with a metadata block. */
const summaryHeadingPattern = /^##\s+summary\s*$/i;

/** Any heading, used to bound the intro block and each section. */
const headingPattern = /^#{1,6}\s/;

/** The metadata field whose value is already written as a one-line description. */
const summaryFieldLabel = 'summary';

/**
 * Inline Markdown removed from a description, in application order.
 *
 * A description is plain text in a meta tag and in a `llms.txt` list item, so link syntax and emphasis markers have to
 * come out while the words they wrap stay.
 */
const inlineReplacements: readonly [RegExp, string][] = [
  [/!\[[^\]]*\]\([^)]*\)/g, ''],
  [/\[([^\]]*)\]\([^)]*\)/g, '$1'],
  [/`([^`]*)`/g, '$1'],
  [/\*\*([^*]+)\*\*/g, '$1'],
  [/__([^_]+)__/g, '$1'],
  [/(?<![\w*])\*([^*]+)\*(?![\w*])/g, '$1'],
  [/<[^>]+>/g, ''],
  [/\s+/g, ' '],
];

/** Longest description emitted before it is cut at a word boundary. */
const descriptionLimit = 240;

/**
 * A one-line description of a page.
 *
 * The strategies are tried in the order that yields the most specific wording: the intro paragraph a reader would read
 * first, then an explicit `Summary` field, then the `## Summary` section, then any prose the page has at all. A page
 * with none of those, such as one whose body is entirely a table, gets no description rather than a misleading one.
 */
export function extractDescription(content: string): string | undefined {
  const lines = withoutByteOrderMark(content).split(/\r?\n/);
  const headingIndex = lines.findIndex((line) => /^#\s+/.test(line));
  const body = lines.slice(headingIndex + 1);
  const intro = untilHeading(body);
  const text =
    firstProse(intro) ?? summaryField(intro) ?? firstProse(summarySection(body)) ?? firstProse(body);

  return text === undefined ? undefined : clampToSentence(text);
}

/** The lines of a block that come before its first heading. */
function untilHeading(lines: readonly string[]): string[] {
  const end = lines.findIndex((line) => headingPattern.test(line.trim()));

  return end === -1 ? [...lines] : lines.slice(0, end);
}

/**
 * The first prose paragraph in a block of lines, as plain text.
 *
 * Blocks that are not prose are skipped while looking for the paragraph and end it once one has started, so a
 * paragraph is never joined to the table or list that follows it.
 */
function firstProse(lines: readonly string[]): string | undefined {
  const paragraph: string[] = [];

  for (const line of lines) {
    const trimmed = line.trim();

    if (trimmed.length === 0 || nonProsePattern.test(trimmed) || metadataLinePattern.test(trimmed)) {
      if (paragraph.length > 0) {
        break;
      }

      continue;
    }

    paragraph.push(trimmed);
  }

  return paragraph.length === 0 ? undefined : clean(paragraph.join(' '));
}

/** The value of the `Summary` metadata field, when the block declares one. */
function summaryField(lines: readonly string[]): string | undefined {
  for (const line of lines) {
    const field = metadataLinePattern.exec(line.trim());

    if (field?.[1].trim().toLowerCase() === summaryFieldLabel && field[2].trim().length > 0) {
      return clean(field[2]);
    }
  }

  return undefined;
}

/** The lines under a `## Summary` heading, up to the next heading. */
function summarySection(body: readonly string[]): string[] {
  const start = body.findIndex((line) => summaryHeadingPattern.test(line.trim()));

  if (start === -1) {
    return [];
  }

  return untilHeading(body.slice(start + 1));
}

/** Markdown reduced to the plain text a description is. */
function clean(text: string): string | undefined {
  const plain = inlineReplacements
    .reduce((carry, [pattern, replacement]) => carry.replace(pattern, replacement), text)
    .trim();

  return plain.length === 0 ? undefined : plain;
}

/**
 * The leading sentence, or a word-boundary cut when there is no sentence break within the limit.
 *
 * A terminator only ends a sentence when the character before it is not a space and the character after it is. That
 * leaves `.NET`, `6.0.2`, and a trailing `*.Extensions.Microsoft.DependencyInjection` intact, which a plainer split on
 * `.` would not.
 */
function clampToSentence(text: string): string {
  const sentence = /[^\s]([.!?])(?=\s|$)/.exec(text);

  if (sentence?.index !== undefined) {
    const end = sentence.index + sentence[0].length;

    if (end <= descriptionLimit) {
      return text.slice(0, end);
    }
  }

  if (text.length <= descriptionLimit) {
    return text;
  }

  const cut = text.lastIndexOf(' ', descriptionLimit);

  return `${text.slice(0, cut === -1 ? descriptionLimit : cut)}...`;
}
