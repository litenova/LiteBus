// The litebus.io site: static output built from the Markdown under content/docs.
import { defineConfig } from "astro/config";

export default defineConfig({
  site: "https://litebus.io",
  output: "static",
  trailingSlash: "never",
  build: { format: "file" },
});
