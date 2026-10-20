import type { RehypeCodeOptions } from 'fumadocs-core/mdx-plugins/rehype-code';
import {
  createHighlighter,
  createJavaScriptRegexEngine,
  type ShikiTransformer,
  type ThemedToken,
} from 'shiki';

/** Neutral theme used only because Shiki requires one; every color it assigns is stripped. */
const neutralTheme = 'min-light';

/** Grammars loaded for fenced code blocks. Anything else falls back to plain text. */
const supportedLangs = ['csharp', 'bash', 'xml', 'json', 'powershell', 'sql', 'yaml'] as const;

/** Property key holding the computed role on a token between the tokens and span hooks. */
const roleKey = Symbol('litebusRole');

/** A token carrying its computed syntax role between transformer hooks. */
type TokenWithRole = ThemedToken & { [roleKey]?: string | null };

/** Languages the role classifier distinguishes. */
type ResolvedLang = 'csharp' | 'bash' | 'xml' | 'json' | 'powershell' | 'sql' | 'yaml' | 'plain';

/** C# control-flow keywords, rendered with the control role. */
const controlWords = new Set([
  'await',
  'return',
  'if',
  'else',
  'throw',
  'foreach',
  'in',
  'switch',
  'case',
  'yield',
  'try',
  'catch',
  'finally',
  'while',
  'for',
  'do',
  'break',
  'continue',
  'goto',
]);

/**
 * C# reserved keywords, rendered with the keyword role when the grammar leaves
 * a token without scopes. Contextual words are deliberately absent: the grammar
 * scopes their keyword uses, and as identifiers they must stay variables.
 */
const keywordWords = new Set([
  'abstract',
  'as',
  'base',
  'bool',
  'byte',
  'char',
  'checked',
  'class',
  'const',
  'decimal',
  'default',
  'delegate',
  'double',
  'enum',
  'event',
  'explicit',
  'extern',
  'false',
  'fixed',
  'float',
  'implicit',
  'int',
  'interface',
  'internal',
  'is',
  'lock',
  'long',
  'namespace',
  'new',
  'null',
  'object',
  'operator',
  'out',
  'override',
  'params',
  'private',
  'protected',
  'public',
  'readonly',
  'ref',
  'sbyte',
  'sealed',
  'short',
  'sizeof',
  'stackalloc',
  'static',
  'string',
  'struct',
  'this',
  'true',
  'typeof',
  'uint',
  'ulong',
  'unchecked',
  'unsafe',
  'ushort',
  'using',
  'virtual',
  'void',
  'volatile',
]);

/** Built-in type names, used to recognize declarations such as `string Name`. */
const builtinTypeWords = new Set([
  'bool',
  'byte',
  'sbyte',
  'short',
  'ushort',
  'int',
  'uint',
  'nint',
  'nuint',
  'long',
  'ulong',
  'float',
  'double',
  'decimal',
  'char',
  'string',
  'object',
  'void',
  'dynamic',
  'var',
]);

/** Type declaration keywords; the identifier after one is a type name. */
const declarationWords = new Set(['class', 'record', 'struct', 'interface', 'enum']);

/** Keywords whose parenthesized argument is a type name, as in `typeof(Program)`. */
const typeOperatorWords = new Set(['typeof', 'sizeof', 'default']);

/** Framework value types rendered with the struct role. */
const structNames = new Set([
  'Guid',
  'CancellationToken',
  'TimeSpan',
  'DateTime',
  'DateTimeOffset',
  'ValueTask',
  'Nullable',
  'ReadOnlySpan',
  'Span',
  'ReadOnlyMemory',
  'Memory',
  'Decimal',
  'Int32',
  'Int64',
  'KeyValuePair',
]);

/** Multi-character operators recognized when scanning for significant neighbors. */
const multiCharOperators = new Set([
  '??=',
  '>>>=',
  '?.[',
  '<<=',
  '>>=',
  '>>>',
  '?.',
  '??',
  '=>',
  '==',
  '!=',
  '<=',
  '>=',
  '&&',
  '||',
  '<<',
  '>>',
  '::',
  '->',
  '++',
  '--',
  '+=',
  '-=',
  '*=',
  '/=',
  '%=',
  '&=',
  '|=',
  '^=',
  '..',
  '</',
  '/>',
]);

/** A non-whitespace lexical unit found by scanning the source around a token. */
interface SourceToken {
  /** The scanned text. */
  text: string;
  /** Offset of the first character. */
  start: number;
  /** Offset just past the last character. */
  end: number;
}

/** Everything a single-token classifier needs besides the token text itself. */
interface TokenContext {
  /** The full source, for neighbor scans. */
  source: string;
  /** Offset of the token's first non-whitespace character. */
  start: number;
  /** Offset just past the token's last non-whitespace character. */
  end: number;
  /** TextMate scopes attached to the token, if any. */
  scopes: string[];
}

/** Returns true for ASCII whitespace. */
function isWhitespaceChar(char: string): boolean {
  return char === ' ' || char === '\t' || char === '\n' || char === '\r' || char === '\f' || char === '\v';
}

/** Returns true for characters that continue a word token: letters, digits, underscore. */
function isWordChar(char: string): boolean {
  return (
    (char >= 'a' && char <= 'z') || (char >= 'A' && char <= 'Z') || (char >= '0' && char <= '9') || char === '_'
  );
}

/** Strips a verbatim `@` prefix so `@class` tests as an identifier. */
function bareName(word: string): string {
  return word.startsWith('@') ? word.slice(1) : word;
}

/** Returns true for identifier-shaped text. */
function isIdentifierName(word: string): boolean {
  return /^[A-Za-z_][A-Za-z0-9_]*$/.test(word);
}

/** Returns true for identifiers starting with an uppercase letter. */
function isPascalName(name: string): boolean {
  return name.length > 0 && name[0] >= 'A' && name[0] <= 'Z';
}

/** Returns true for text made only of punctuation and operator characters. */
function isPunctuationText(word: string): boolean {
  return /^[(){}\[\]<>=!+*\/%&|^~?:;,.#$@-]+$/.test(word);
}

/** Returns true when any scope equals the prefix or starts with it. */
function hasScopePrefix(scopes: string[], prefix: string): boolean {
  return scopes.some((scope) => scope === prefix || scope.startsWith(prefix));
}

/** Returns true when any scope contains the given fragment. */
function hasScopePart(scopes: string[], part: string): boolean {
  return scopes.some((scope) => scope.includes(part));
}

/** Reads the significant token ending at offset, skipping whitespace backwards. */
function readTokenBackward(source: string, offset: number): SourceToken | null {
  let end = offset;
  while (end > 0 && isWhitespaceChar(source[end - 1])) {
    end--;
  }
  if (end <= 0) {
    return null;
  }
  if (isWordChar(source[end - 1])) {
    let start = end - 1;
    while (start > 0 && isWordChar(source[start - 1])) {
      start--;
    }
    if (start > 0 && source[start - 1] === '@') {
      start--;
    }
    return { text: source.slice(start, end), start, end };
  }
  for (const size of [3, 2, 1]) {
    if (end - size < 0) {
      continue;
    }
    const text = source.slice(end - size, end);
    if (size === 1 || multiCharOperators.has(text)) {
      return { text, start: end - size, end };
    }
  }
  return null;
}

/** Reads the significant token starting at offset, skipping whitespace forwards. */
function readTokenForward(source: string, offset: number): SourceToken | null {
  let start = offset;
  while (start < source.length && isWhitespaceChar(source[start])) {
    start++;
  }
  if (start >= source.length) {
    return null;
  }
  if (isWordChar(source[start]) || source[start] === '@') {
    let end = start + 1;
    while (end < source.length && isWordChar(source[end])) {
      end++;
    }
    return { text: source.slice(start, end), start, end };
  }
  for (const size of [3, 2, 1]) {
    const text = source.slice(start, start + size);
    if (text.length < size) {
      continue;
    }
    if (size === 1 || multiCharOperators.has(text)) {
      return { text, start, end: start + size };
    }
  }
  return null;
}

/**
 * Scans forward from a `<` for its matching `>`, so generic argument lists can
 * be told apart from comparisons. Returns the offset just past the `>`, or null.
 */
function findGenericEnd(source: string, openIndex: number): number | null {
  let angle = 0;
  let round = 0;
  let square = 0;
  let newlines = 0;
  const limit = Math.min(source.length, openIndex + 2000);
  for (let index = openIndex; index < limit; index++) {
    const char = source[index];
    if (char === '\n') {
      newlines++;
      if (newlines > 5) {
        return null;
      }
      continue;
    }
    if (char === '"' || char === "'" || char === ';' || char === '{' || char === '}') {
      return null;
    }
    if (char === '/' && (source[index + 1] === '/' || source[index + 1] === '*')) {
      return null;
    }
    if (char === '<') {
      angle++;
    } else if (char === '>') {
      angle--;
      if (angle === 0 && round === 0 && square === 0) {
        return index + 1;
      }
      if (angle < 0) {
        return null;
      }
    } else if (char === '(') {
      round++;
    } else if (char === ')') {
      round--;
      if (round < 0) {
        return null;
      }
    } else if (char === '[') {
      square++;
    } else if (char === ']') {
      square--;
      if (square < 0) {
        return null;
      }
    }
  }
  return null;
}

/**
 * Scans backward from a `>` for its matching `<`. Returns the `<` offset, or null.
 */
function findGenericOpen(source: string, closeIndex: number): number | null {
  let angle = 0;
  let round = 0;
  let square = 0;
  let newlines = 0;
  const limit = Math.max(0, closeIndex - 2000);
  for (let index = closeIndex; index >= limit; index--) {
    const char = source[index];
    if (char === '\n') {
      newlines++;
      if (newlines > 5) {
        return null;
      }
    } else if (char === '"' || char === "'" || char === ';' || char === '{' || char === '}') {
      return null;
    } else if (char === '>') {
      angle++;
    } else if (char === '<') {
      angle--;
      if (angle === 0 && round === 0 && square === 0) {
        return index;
      }
      if (angle < 0) {
        return null;
      }
    } else if (char === ')') {
      round++;
    } else if (char === '(') {
      round--;
      if (round < 0) {
        return null;
      }
    } else if (char === ']') {
      square++;
    } else if (char === '[') {
      square--;
      if (square < 0) {
        return null;
      }
    }
  }
  return null;
}

/**
 * Returns true when the identifier ending at offset is invoked: followed by `(`
 * or by a generic argument list and `(`.
 */
function isCallAhead(source: string, offset: number): boolean {
  const next = readTokenForward(source, offset);
  if (next === null) {
    return false;
  }
  if (next.text === '(') {
    return true;
  }
  if (next.text !== '<') {
    return false;
  }
  const end = findGenericEnd(source, next.start);
  if (end === null) {
    return false;
  }
  return readTokenForward(source, end)?.text === '(';
}

/** Maps a type name to the interface, struct, or class role. */
function typeRole(name: string): string {
  if (/^I[A-Z]/.test(name)) {
    return 'ifc';
  }
  return structNames.has(name) ? 'st' : 'cls';
}

/**
 * Returns true when the identifier starting at offset belongs to an object
 * creation chain: `new Foo`, `new Foo.Bar(...)`, or `new Foo<T>.Bar(...)`.
 */
function isInObjectCreation(source: string, start: number): boolean {
  let cursor = start;
  for (let depth = 0; depth < 20; depth++) {
    const token = readTokenBackward(source, cursor);
    if (token === null) {
      return false;
    }
    if (token.text === 'new') {
      return true;
    }
    if (token.text !== '.' && token.text !== '?.') {
      return false;
    }
    const receiver = readTokenBackward(source, token.start);
    if (receiver === null) {
      return false;
    }
    if (receiver.text.endsWith('>')) {
      const open = findGenericOpen(source, receiver.end - 1);
      if (open === null) {
        return false;
      }
      const before = readTokenBackward(source, open);
      if (before === null || !isIdentifierName(bareName(before.text))) {
        return false;
      }
      cursor = before.start;
      continue;
    }
    if (!isIdentifierName(bareName(receiver.text))) {
      return false;
    }
    cursor = receiver.start;
  }
  return false;
}

/**
 * Returns true when a bracketed identifier sits in attribute position: directly
 * after `[`, or after a comma whose enclosing bracket is `[` rather than `(`.
 */
function isAttributePosition(source: string, prev: SourceToken): boolean {
  if (prev.text === '[') {
    return true;
  }
  if (prev.text !== ',') {
    return false;
  }
  let depth = 0;
  let cursor = prev.start;
  for (let step = 0; step < 50; step++) {
    const token = readTokenBackward(source, cursor);
    if (token === null) {
      return false;
    }
    if (token.text === ';' || token.text === '{' || token.text === '}') {
      return false;
    }
    if (token.text === ']' || token.text === ')') {
      depth++;
    } else if (token.text === '[' || token.text === '(') {
      if (depth === 0) {
        return token.text === '[';
      }
      depth--;
    }
    cursor = token.start;
  }
  return false;
}

/**
 * Returns true when the brackets opening at offset hold only commas, as in
 * array type ranks (`[]`, `[,]`), rather than an index expression.
 */
function isEmptyBracketPair(source: string, offset: number): boolean {
  let cursor = offset;
  for (let step = 0; step < 10; step++) {
    const token = readTokenForward(source, cursor);
    if (token === null) {
      return false;
    }
    if (token.text === ']') {
      return true;
    }
    if (token.text !== ',') {
      return false;
    }
    cursor = token.end;
  }
  return false;
}

/**
 * Returns true when the dotted chain ending at offset is a namespace or using
 * name, so every segment is a type rather than a member access.
 */
function isUsingOrNamespaceChain(source: string, start: number): boolean {
  let cursor = start;
  for (let step = 0; step < 20; step++) {
    const dot = readTokenBackward(source, cursor);
    if (dot === null || (dot.text !== '.' && dot.text !== '?.')) {
      return false;
    }
    const segment = readTokenBackward(source, dot.start);
    if (segment === null || !isIdentifierName(bareName(segment.text))) {
      return false;
    }
    cursor = segment.start;
    const head = readTokenBackward(source, cursor);
    if (head === null) {
      return false;
    }
    if (head.text === 'using' || head.text === 'namespace') {
      return true;
    }
    if (head.text === 'static' || head.text === 'global') {
      const outer = readTokenBackward(source, head.start);
      return outer !== null && (outer.text === 'using' || outer.text === 'namespace');
    }
    if (head.text === '=') {
      // A using alias target, as in `using Foo = Bar.Baz;`.
      const alias = readTokenBackward(source, head.start);
      if (alias === null || !isIdentifierName(bareName(alias.text))) {
        return false;
      }
      const outer = readTokenBackward(source, alias.start);
      return outer !== null && outer.text === 'using';
    }
    if (head.text !== '.' && head.text !== '?.') {
      return false;
    }
    cursor = head.end;
  }
  return false;
}

/**
 * Returns true when an identifier sits in parameter position: a type-like token
 * before it and a parameter-list terminator after it.
 */
function isParameterPosition(prev: SourceToken | null, next: SourceToken | null): boolean {
  if (prev === null || next === null) {
    return false;
  }
  if (next.text !== ',' && next.text !== ')' && next.text !== '=' && next.text !== 'in') {
    return false;
  }
  if (builtinTypeWords.has(prev.text) || prev.text === '>' || prev.text === '?' || prev.text === ']') {
    return true;
  }
  const name = bareName(prev.text);
  return isIdentifierName(name) && isPascalName(name);
}

/**
 * Classifies one C# token by scope first and by name and position second.
 * Returns the role suffix, or null for plain text.
 */
function classifyCSharp(word: string, context: TokenContext): string | null {
  const { source, scopes } = context;
  const name = bareName(word);
  const identifier = isIdentifierName(name);
  const pascal = identifier && isPascalName(name);

  if (hasScopePrefix(scopes, 'comment') || hasScopePart(scopes, 'definition.comment')) {
    return 'com';
  }
  if (
    hasScopePrefix(scopes, 'string') ||
    hasScopePart(scopes, '.string.') ||
    hasScopePart(scopes, 'definition.string') ||
    hasScopePart(scopes, 'definition.char') ||
    hasScopePrefix(scopes, 'constant.character')
  ) {
    return 'str';
  }
  if (hasScopePrefix(scopes, 'constant.numeric')) {
    return 'num';
  }
  if (hasScopePrefix(scopes, 'keyword.control') || (identifier && controlWords.has(name))) {
    return 'ctl';
  }
  if (scopes.some((scope) => scope === 'keyword' || (scope.startsWith('keyword.') && !scope.startsWith('keyword.operator')))) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'keyword.operator') && identifier) {
    // Word operators such as `with`, `new`, `as`, and query words stay keywords;
    // symbol operators fall through to punctuation.
    return controlWords.has(name) ? 'ctl' : 'kw';
  }
  if (hasScopePrefix(scopes, 'constant')) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'storage')) {
    return 'kw';
  }
  if (identifier && keywordWords.has(name)) {
    return 'kw';
  }
  if (hasScopePart(scopes, 'attribute')) {
    return 'attr';
  }

  const prev = readTokenBackward(source, context.start);
  const next = readTokenForward(source, context.end);

  if (
    pascal &&
    prev !== null &&
    next !== null &&
    (next.text === ']' || next.text === '(' || next.text === ',' || next.text === '<') &&
    isAttributePosition(source, prev)
  ) {
    return 'attr';
  }
  // Object creation and declarations precede the method rule: `Supplied` in
  // `new MessageIdentity.Supplied(...)` is a type, not a method.
  if (identifier && isInObjectCreation(source, context.start)) {
    return typeRole(name);
  }
  if (identifier && prev !== null && declarationWords.has(prev.text)) {
    return typeRole(name);
  }
  if (hasScopePart(scopes, 'entity.name.function') && !isPunctuationText(word)) {
    return 'fn';
  }
  if (identifier && next !== null && (next.text === '(' || (next.text === '<' && isCallAhead(source, context.end)))) {
    return 'fn';
  }
  if (name === '_') {
    return 'var';
  }
  if (identifier && name.startsWith('_')) {
    return 'fld';
  }
  if (hasScopePart(scopes, 'entity.name.variable.field')) {
    return 'fld';
  }
  if (identifier && isParameterPosition(prev, next)) {
    return 'var';
  }
  if (hasScopePart(scopes, 'entity.name.type') || hasScopePart(scopes, 'variable.other.alias')) {
    return typeRole(identifier ? name : word);
  }
  const contextualType = identifier && pascal ? contextualTypeRole(source, prev, next, name) : null;
  if (contextualType !== null) {
    return contextualType;
  }
  if (
    hasScopePart(scopes, 'entity.name.variable.property') ||
    hasScopePart(scopes, 'entity.name.variable.event') ||
    hasScopePart(scopes, 'entity.name.variable.enum-member') ||
    hasScope(scopes, 'variable.other.object.property.cs')
  ) {
    return 'prop';
  }
  if (hasScopePrefix(scopes, 'variable')) {
    return name === 'this' || name === 'base' ? 'kw' : 'var';
  }
  if (hasScopePart(scopes, 'entity.name.variable')) {
    return 'var';
  }
  if (pascal) {
    const property = positionalPropertyRole(source, context.start, prev, next, name);
    if (property !== null) {
      return property;
    }
  }
  if (identifier) {
    if (next !== null && next.text === ':' && (prev === null || prev.text !== 'case')) {
      return 'var';
    }
    if (name[0] >= 'a' && name[0] <= 'z') {
      return 'var';
    }
  }
  if (hasScopePrefix(scopes, 'punctuation') || hasScopePrefix(scopes, 'keyword.operator')) {
    return 'pun';
  }
  if (isPunctuationText(word)) {
    return 'pun';
  }
  return null;
}

/** Returns true when the scopes contain one exact scope name. */
function hasScope(scopes: string[], scope: string): boolean {
  return scopes.includes(scope);
}

/**
 * Classifies a PascalCase identifier by position when the grammar gives it no
 * type scope: base lists, generic arguments, patterns, and static access.
 */
function contextualTypeRole(
  source: string,
  prev: SourceToken | null,
  next: SourceToken | null,
  name: string,
): string | null {
  if (prev === null) {
    return null;
  }
  if (prev.text === '(') {
    const before = readTokenBackward(source, prev.start);
    if (before !== null && typeOperatorWords.has(before.text)) {
      return typeRole(name);
    }
  }
  if (prev.text === 'is' || prev.text === 'as') {
    return typeRole(name);
  }
  if (prev.text === 'not') {
    const before = readTokenBackward(source, prev.start);
    if (before !== null && before.text === 'is') {
      return typeRole(name);
    }
  }
  if ((prev.text === 'using' || prev.text === 'namespace') && !builtinTypeWords.has(name)) {
    return typeRole(name);
  }
  if (prev.text === 'static') {
    const before = readTokenBackward(source, prev.start);
    if (before !== null && before.text === 'using') {
      return typeRole(name);
    }
  }
  if (next === null) {
    return null;
  }
  if (prev.text === 'case' && next.text !== ':') {
    return typeRole(name);
  }
  if (prev.text === ':' && next.text !== '(') {
    return typeRole(name);
  }
  if (prev.text === '<' && (next.text === ',' || next.text.startsWith('>') || next.text === '[')) {
    return typeRole(name);
  }
  if (prev.text === ',' && next.text !== '=') {
    return typeRole(name);
  }
  if (isIdentifierName(bareName(next.text))) {
    // Two adjacent identifiers declare `Type name`; keywords such as `with`
    // in `Item with { ... }` end an expression instead.
    const nextName = bareName(next.text);
    if (nextName !== 'with' && !keywordWords.has(nextName) && !controlWords.has(nextName)) {
      return typeRole(name);
    }
  }
  if (next.text === '[' && isEmptyBracketPair(source, next.end)) {
    // `Foo[]` is an array type; `Foo[x]` is an element access.
    return typeRole(name);
  }
  if (next.text === '<' && findGenericEnd(source, next.start) !== null) {
    return typeRole(name);
  }
  if (
    prev.text !== '.' &&
    prev.text !== '?.' &&
    prev.text !== '->' &&
    (next.text === '.' || next.text === '?.' || next.text === '->')
  ) {
    const member = readTokenForward(source, next.end);
    if (member !== null) {
      const memberName = bareName(member.text);
      if (isIdentifierName(memberName) && isPascalName(memberName)) {
        // Static access such as `Task.FromResult` or `Guid.NewGuid`.
        return typeRole(name);
      }
    }
  }
  if (prev.text === '::') {
    return typeRole(name);
  }
  return null;
}

/**
 * Classifies a PascalCase identifier by property position: member access,
 * object initializer and `with` bodies, and member declarations.
 */
function positionalPropertyRole(
  source: string,
  start: number,
  prev: SourceToken | null,
  next: SourceToken | null,
  name: string,
): string | null {
  if (prev !== null && (prev.text === '.' || prev.text === '?.' || prev.text === '->')) {
    if (isUsingOrNamespaceChain(source, start)) {
      return typeRole(name);
    }
    if (next === null) {
      return 'prop';
    }
    if (next.text === '<' && findGenericEnd(source, next.start) !== null) {
      // A generic member access without a call is a nested generic type.
      return typeRole(name);
    }
    if (next.text === '.' || next.text === '?.' || next.text === '->') {
      const member = readTokenForward(source, next.end);
      if (member !== null && isIdentifierName(bareName(member.text)) && isCallAhead(source, member.end)) {
        // `.Contracts.Register(`: a property followed by a method call.
        return 'prop';
      }
      // A dotted middle segment such as `Text` in `System.Text.Json`.
      return typeRole(name);
    }
    if (isIdentifierName(bareName(next.text))) {
      // Keywords end the chain: `Immediate with { ... }`, `Count is int`.
      const nextName = bareName(next.text);
      if (nextName === 'with' || keywordWords.has(nextName) || controlWords.has(nextName)) {
        return 'prop';
      }
      // `.Contracts.Register(` is a property; `.Inner x` declares a nested type.
      return isCallAhead(source, next.end) ? 'prop' : typeRole(name);
    }
    return 'prop';
  }
  if (next !== null && next.text === '=') {
    return 'prop';
  }
  if (next !== null && next.text === '{') {
    return 'prop';
  }
  if (next !== null && next.text === '=>') {
    return 'prop';
  }
  return null;
}

/** Returns true when the token starts a command: first on its line or after a chain operator. */
function isCommandPosition(source: string, start: number): boolean {
  let index = start - 1;
  while (index >= 0 && (source[index] === ' ' || source[index] === '\t' || source[index] === '\r')) {
    index--;
  }
  if (index < 0 || source[index] === '\n') {
    return true;
  }
  const prev = readTokenBackward(source, start);
  return (
    prev !== null &&
    (prev.text === '&&' ||
      prev.text === '||' ||
      prev.text === ';' ||
      prev.text === '|' ||
      prev.text === '&' ||
      prev.text === '(' ||
      prev.text === '{' ||
      prev.text === '`')
  );
}

/** Returns true for command-shaped words: paths and names, not flags or expansions. */
function isBashWord(word: string): boolean {
  return /^[A-Za-z0-9_.\/~][A-Za-z0-9_.\/~-]*$/.test(word);
}

/**
 * Classifies one shell token: command names, flags, quoted strings, comments,
 * and variables. Unquoted arguments stay plain.
 */
function classifyBash(word: string, context: TokenContext): string | null {
  const { source, scopes } = context;
  if (hasScopePrefix(scopes, 'comment')) {
    return 'com';
  }
  if (hasScopePart(scopes, 'assignment')) {
    if (hasScopePrefix(scopes, 'variable')) {
      return 'var';
    }
    if (hasScopePrefix(scopes, 'punctuation') || isPunctuationText(word)) {
      return 'pun';
    }
  }
  if (hasScopePart(scopes, 'entity.name.function') || hasScopePrefix(scopes, 'support.function')) {
    return 'fn';
  }
  if (isCommandPosition(source, context.start) && isBashWord(word) && !word.includes('=')) {
    return 'fn';
  }
  // Flags precede strings: the grammar may scope them as unquoted arguments.
  if (/^--?[A-Za-z]/.test(word)) {
    return 'kw';
  }
  if (
    hasScopePrefix(scopes, 'string.quoted') ||
    hasScopePrefix(scopes, 'string.interpolated') ||
    hasScopePrefix(scopes, 'string.regexp') ||
    hasScopePrefix(scopes, 'constant.character')
  ) {
    return 'str';
  }
  if (hasScopePrefix(scopes, 'string')) {
    return null;
  }
  if (scopes.some((scope) => scope === 'keyword' || (scope.startsWith('keyword.') && !scope.startsWith('keyword.operator')))) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'keyword.operator')) {
    return /^[A-Za-z]/.test(word) ? 'kw' : 'pun';
  }
  if (hasScopePrefix(scopes, 'storage')) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'variable')) {
    return 'var';
  }
  if (hasScopePrefix(scopes, 'constant.numeric')) {
    return 'num';
  }
  if (hasScopePrefix(scopes, 'constant')) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'punctuation') || isPunctuationText(word)) {
    return 'pun';
  }
  return null;
}

/**
 * Classifies one XML token: tag names, attribute names and values, comments,
 * and punctuation. Element text stays plain.
 */
function classifyXml(word: string, context: TokenContext): string | null {
  const { source, scopes } = context;
  if (hasScopePrefix(scopes, 'comment')) {
    return 'com';
  }
  if (hasScopePart(scopes, 'entity.name.tag')) {
    return 'cls';
  }
  if (hasScopePart(scopes, 'attribute-name')) {
    return 'prop';
  }
  if (hasScopePrefix(scopes, 'string') || hasScopePrefix(scopes, 'constant.character')) {
    return 'str';
  }
  if (hasScopePrefix(scopes, 'keyword')) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'variable')) {
    return 'var';
  }
  if (hasScopePrefix(scopes, 'constant.numeric')) {
    return 'num';
  }
  if (hasScopePrefix(scopes, 'constant')) {
    return 'str';
  }
  if (hasScopePrefix(scopes, 'punctuation')) {
    return 'pun';
  }
  const name = bareName(word);
  if (isIdentifierName(name) || /^[A-Za-z0-9_.:-]+$/.test(word)) {
    const prev = readTokenBackward(source, context.start);
    const next = readTokenForward(source, context.end);
    if (prev !== null && (prev.text === '<' || prev.text === '</')) {
      return 'cls';
    }
    if (next !== null && next.text === '=') {
      return 'prop';
    }
  }
  if (isPunctuationText(word)) {
    return 'pun';
  }
  return null;
}

/**
 * Classifies one JSON token: keys, strings, numbers, literals, and punctuation.
 */
function classifyJson(word: string, context: TokenContext): string | null {
  const { source, scopes } = context;
  if (hasScopePrefix(scopes, 'comment')) {
    return 'com';
  }
  if (hasScopePart(scopes, 'property-name')) {
    return 'prop';
  }
  if (hasScopePrefix(scopes, 'string')) {
    const next = readTokenForward(source, context.end);
    return next !== null && next.text === ':' ? 'prop' : 'str';
  }
  if (hasScopePrefix(scopes, 'constant.numeric')) {
    return 'num';
  }
  if (hasScopePrefix(scopes, 'constant.character')) {
    return 'str';
  }
  if (hasScopePrefix(scopes, 'constant')) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'punctuation') || isPunctuationText(word)) {
    return 'pun';
  }
  return null;
}

/**
 * Classifies one token with the generic roles shared by PowerShell, SQL, and YAML.
 *
 * These grammars share the standard TextMate scopes for comments, strings, numbers, keywords, functions, and
 * punctuation, so one classifier maps them to the generic roles. Names the grammar marks as entities, types, or
 * variables render as properties.
 */
function classifyGeneric(word: string, context: TokenContext): string | null {
  const { scopes } = context;
  if (hasScopePrefix(scopes, 'comment')) {
    return 'com';
  }
  if (hasScopePrefix(scopes, 'string') || hasScopePrefix(scopes, 'constant.character')) {
    return 'str';
  }
  if (hasScopePrefix(scopes, 'constant.numeric')) {
    return 'num';
  }
  if (hasScopePart(scopes, 'entity.name.function') || hasScopePrefix(scopes, 'support.function')) {
    return 'fn';
  }
  if (
    scopes.some((scope) => scope === 'keyword' || (scope.startsWith('keyword.') && !scope.startsWith('keyword.operator')))
  ) {
    return 'kw';
  }
  if (hasScopePrefix(scopes, 'keyword.operator')) {
    return /^[A-Za-z]/.test(word) ? 'kw' : 'pun';
  }
  if (hasScopePrefix(scopes, 'keyword') || hasScopePrefix(scopes, 'storage') || hasScopePrefix(scopes, 'constant')) {
    return 'kw';
  }
  if (
    hasScopePart(scopes, 'entity.name') ||
    hasScopePrefix(scopes, 'support.type') ||
    hasScopePrefix(scopes, 'support.class') ||
    hasScopePrefix(scopes, 'variable') ||
    hasScopePart(scopes, 'variable.')
  ) {
    return 'prop';
  }
  if (hasScopePrefix(scopes, 'punctuation') || isPunctuationText(word)) {
    return 'pun';
  }
  return null;
}

/** Normalizes fence info strings and aliases to one classifier language. */
function normalizeLang(lang: string | undefined): ResolvedLang {
  switch ((lang ?? '').toLowerCase()) {
    case 'csharp':
    case 'c#':
    case 'cs':
      return 'csharp';
    case 'bash':
    case 'sh':
    case 'shell':
    case 'shellscript':
    case 'zsh':
      return 'bash';
    case 'xml':
      return 'xml';
    case 'json':
      return 'json';
    case 'powershell':
    case 'pwsh':
      return 'powershell';
    case 'sql':
      return 'sql';
    case 'yaml':
    case 'yml':
      return 'yaml';
    default:
      return 'plain';
  }
}

/** Collects every TextMate scope name attached to a token. */
function collectScopes(token: ThemedToken): string[] {
  const scopes: string[] = [];
  for (const explanation of token.explanation ?? []) {
    for (const scope of explanation.scopes) {
      scopes.push(scope.scopeName);
    }
  }
  return scopes;
}

/** Classifies one token for the given language. Returns the role, or null for plain text. */
function classifyToken(token: ThemedToken, source: string, lang: ResolvedLang): string | null {
  const trimmed = token.content.trim();
  if (trimmed.length === 0) {
    return null;
  }
  const leading = token.content.length - token.content.trimStart().length;
  const start = token.offset + leading;
  const context: TokenContext = {
    source,
    start,
    end: start + trimmed.length,
    scopes: collectScopes(token),
  };
  switch (lang) {
    case 'csharp':
      return classifyCSharp(trimmed, context);
    case 'bash':
      return classifyBash(trimmed, context);
    case 'xml':
      return classifyXml(trimmed, context);
    case 'json':
      return classifyJson(trimmed, context);
    case 'powershell':
    case 'sql':
    case 'yaml':
      return classifyGeneric(trimmed, context);
    case 'plain':
      return null;
  }
}

/**
 * Classifies every token on one line and merges adjacent tokens with the same
 * role into single spans. Whitespace adopts the previous token's role so spacing
 * never splits a span. The role is stored on each token for the span hook.
 */
function mergeLine(line: ThemedToken[], source: string, lang: ResolvedLang): ThemedToken[] {
  if (line.length === 0) {
    return line;
  }
  let previous: string | null = null;
  const effective = line.map((token) => {
    if (token.content.trim().length === 0) {
      return previous;
    }
    previous = classifyToken(token, source, lang);
    return previous;
  });
  const merged: TokenWithRole[] = [];
  for (let index = 0; index < line.length; index++) {
    const token = line[index];
    const role = effective[index];
    const last = merged[merged.length - 1];
    if (last !== undefined && last[roleKey] === role) {
      last.content += token.content;
      if (token.explanation !== undefined) {
        last.explanation = [...(last.explanation ?? []), ...token.explanation];
      }
    } else {
      merged.push({ ...token, [roleKey]: role });
    }
  }
  return merged;
}

/**
 * Maps Shiki tokens to the site's role classes and strips every inline color.
 * The `pre` keeps its metadata attributes but loses the theme class and style.
 */
const roleTransformer: ShikiTransformer = {
  name: 'litebus:roles',
  tokens(lineTokens) {
    const lang = normalizeLang(this.options.lang);
    return lineTokens.map((line) => mergeLine(line, this.source, lang));
  },
  span(node, _line, _column, _lineElement, token) {
    const stored = (token as TokenWithRole)[roleKey];
    const role = stored === undefined ? classifyToken(token, this.source, normalizeLang(this.options.lang)) : stored;
    node.properties = role === null ? {} : { class: `tk-${role}` };
    return node;
  },
  line(node) {
    node.properties = { class: 'line' };
    return node;
  },
  code(node) {
    node.properties = {};
    return node;
  },
  pre(node) {
    const properties = node.properties ?? {};
    delete properties.style;
    properties.class = 'code';
    // The block head shows the fence language, so it travels with the element instead of being re-read from source.
    properties['data-lang'] = typeof this.options.lang === 'string' ? this.options.lang : '';
    node.properties = properties;
    return node;
  },
};

/** Options for @fumadocs/local-md's rehypeCodeOptions, so Markdown code blocks get the C# role classes. */
export const rehypeCodeOptions: RehypeCodeOptions = {
  // local-md spreads these over fumadocs' defaults, which carry a light and dark theme pair. A single `theme` would
  // leave that pair in place unloaded, so the neutral theme fills both slots; the role classes do the colouring.
  themes: { light: neutralTheme, dark: neutralTheme },
  langs: [...supportedLangs],
  lazy: false,
  defaultLanguage: 'plaintext',
  fallbackLanguage: 'plaintext',
  includeExplanation: 'scopeName',
  transformers: [roleTransformer],
  tab: false,
  icon: false,
};

/**
 * The process-wide highlighter for `highlightToHtml`, created once. It uses the
 * same JavaScript regex engine fumadocs uses so both paths tokenize identically.
 */
const sharedHighlighter = createHighlighter({
  engine: createJavaScriptRegexEngine(),
  themes: [neutralTheme],
  langs: [...supportedLangs],
});

/** Highlights a snippet to an HTML string with the same classes, for server components (the home page). */
export async function highlightToHtml(
  code: string,
  lang: 'csharp' | 'bash' | 'xml' | 'json' = 'csharp',
): Promise<string> {
  const highlighter = await sharedHighlighter;
  return highlighter.codeToHtml(code, {
    lang,
    theme: neutralTheme,
    structure: 'classic',
    includeExplanation: 'scopeName',
    transformers: [roleTransformer],
  });
}
