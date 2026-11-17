// Search and sharing metadata: canonical URLs, meta descriptions, the social image,
// and the schema.org objects every page writes as JSON-LD.
import {
  companyName,
  companyUrl,
  latestRelease,
  repositoryUrl,
  siteDescription,
  siteName,
  siteOgImage,
  siteUrl,
} from "./site.ts";
import { breadcrumbs, type DocPage } from "./docs.ts";

/** The absolute URL of a site path. */
export function absoluteUrl(pathname: string): string {
  return new URL(pathname, siteUrl).toString();
}

/**
 * A page title as "Page - Section - LiteBus", or "Page - LiteBus" without a
 * section. The section is dropped when the full form passes 60 characters, so
 * the page title itself is never truncated and titles stay unique.
 *
 * The home page does not use this helper; it uses the site title directly.
 */
export function pageTitle(title: string, section?: string): string {
  const clean = title.replace(/\s+/g, " ").trim();
  const withSection = section ? `${clean} - ${section} - LiteBus` : `${clean} - LiteBus`;
  if (withSection.length <= 60) return withSection;
  return `${clean} - LiteBus`;
}

/**
 * A meta description of at most 160 characters: the text up to its last sentence end
 * that fits, otherwise up to the last whole word, with an ellipsis written as three dots.
 */
export function metaDescription(text: string, limit = 160): string {
  const clean = text.replace(/\s+/g, " ").trim();
  if (clean.length <= limit) return clean;
  const cut = clean.slice(0, limit);
  const sentence = cut.lastIndexOf(". ");
  if (sentence >= 80) return cut.slice(0, sentence + 1);
  const word = cut.slice(0, limit - 3).lastIndexOf(" ");
  return `${cut.slice(0, word > 0 ? word : limit - 3).replace(/[,;:]$/, "")}...`;
}

export const socialImage = {
  url: absoluteUrl(siteOgImage),
  width: 1280,
  height: 640,
  alt: `${siteName} - ${siteDescription}`,
};

export const organization = {
  "@type": "Organization",
  "@id": `${siteUrl}/#organization`,
  name: companyName,
  url: companyUrl,
  sameAs: [repositoryUrl],
};

export const website = {
  "@type": "WebSite",
  "@id": `${siteUrl}/#website`,
  name: siteName,
  url: `${siteUrl}/`,
  description: siteDescription,
  inLanguage: "en",
  publisher: { "@id": `${siteUrl}/#organization` },
};

export const softwareSourceCode = {
  "@type": "SoftwareSourceCode",
  "@id": `${siteUrl}/#software`,
  name: siteName,
  codeRepository: repositoryUrl,
  programmingLanguage: "C#",
  license: "https://opensource.org/licenses/MIT",
  runtimePlatform: ".NET",
  version: latestRelease,
  author: { "@id": `${siteUrl}/#organization` },
};

/** A docs page as a technical article in the LiteBus website. */
export function techArticle(page: DocPage): Record<string, unknown> {
  return {
    "@type": "TechArticle",
    "@id": `${absoluteUrl(page.url)}#article`,
    headline: page.title,
    description: metaDescription(page.description),
    url: absoluteUrl(page.url),
    inLanguage: "en",
    articleSection: page.section,
    version: latestRelease,
    dateModified: page.lastModified,
    mainEntityOfPage: { "@type": "WebPage", "@id": `${absoluteUrl(page.url)}#webpage` },
    image: socialImage.url,
    isPartOf: { "@id": `${siteUrl}/#website` },
    about: { "@id": `${siteUrl}/#software` },
    encoding: {
      "@type": "MediaObject",
      contentUrl: absoluteUrl(page.mdUrl),
      encodingFormat: "text/markdown",
    },
    publisher: { "@id": `${siteUrl}/#organization` },
    author: { "@id": `${siteUrl}/#organization` },
  };
}

/** A breadcrumb trail for search engines, from the visible trail. The visible
 * trail leaves the current page without a URL because it is not a link; here
 * every entry needs its URL, so the current page resolves to its own. */
export function breadcrumbList(page: DocPage): Record<string, unknown> {
  const items = breadcrumbs(page);
  return {
    "@type": "BreadcrumbList",
    itemListElement: items.map((item, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: item.label,
      item: absoluteUrl(item.url ?? page.url),
    })),
  };
}
