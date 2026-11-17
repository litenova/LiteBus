// Tests for the code-block figures: language labels and the header strip with
// its copy button.
import { describe, it } from "node:test";
import assert from "node:assert";
import { decorateCodeBlocks, languageLabel } from "./code-blocks.ts";

describe("languageLabel", () => {
  it("names the documented languages", () => {
    assert.strictEqual(languageLabel("csharp"), "C#");
    assert.strictEqual(languageLabel("bash"), "Shell");
    assert.strictEqual(languageLabel("shell"), "Shell");
    assert.strictEqual(languageLabel("powershell"), "PowerShell");
    assert.strictEqual(languageLabel("xml"), "XML");
    assert.strictEqual(languageLabel("json"), "JSON");
    assert.strictEqual(languageLabel("yaml"), "YAML");
    assert.strictEqual(languageLabel("sql"), "SQL");
    assert.strictEqual(languageLabel("plaintext"), "Text");
  });

  it("passes unknown ids through", () => {
    assert.strictEqual(languageLabel("mylang"), "mylang");
  });
});

describe("decorateCodeBlocks", () => {
  it("wraps a highlighted block in a figure with a header strip", () => {
    const html = decorateCodeBlocks(
      '<pre class="shiki astro-code" data-language="csharp"><code><span>a &lt; b</span></code></pre>',
    );
    assert.ok(html.includes('<figure class="code-block" data-language="csharp">'), html);
    assert.ok(html.includes('<span class="code-lang">C#</span>'), html);
    assert.ok(
      html.includes(
        '<button type="button" class="copy" data-copy="a &lt; b" aria-label="Copy code">Copy</button>',
      ),
      html,
    );
  });

  it("labels a block without a language as text", () => {
    const html = decorateCodeBlocks('<pre class="astro-code"><code>x</code></pre>');
    assert.ok(html.includes('<span class="code-lang">Text</span>'), html);
  });

  it("leaves plain pre elements alone", () => {
    const plain = "<pre><code>plain</code></pre>";
    assert.strictEqual(decorateCodeBlocks(plain), plain);
  });
});
