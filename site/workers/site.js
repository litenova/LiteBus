// Serves each documentation page as HTML or Markdown from one URL.
//
// A client that requests a documentation URL with an `Accept` header preferring
// `text/markdown` receives that page's Markdown source, while browsers keep
// receiving HTML. The `.md` URLs stay the stable, documented form of the
// Markdown; this script only selects between the two variants of one URL from
// the `Accept` header, so caches keep them apart with `Vary: Accept`.
//
// Cloudflare serves the built site from static assets and runs this script
// first for documentation paths only (see `run_worker_first` in
// `wrangler.jsonc`). The `ASSETS` binding, `main`, and `run_worker_first`
// with path patterns behave as documented at:
// https://developers.cloudflare.com/workers/static-assets/binding/
// https://developers.cloudflare.com/workers/static-assets/routing/worker-script/

/**
 * Reports whether an `Accept` header prefers Markdown over HTML.
 *
 * Returns true only when `text/markdown` carries a strictly higher q-value
 * than `text/html`, or when `text/markdown` is present while `text/html` and
 * the any-type range (star slash star) are both absent. A client that accepts anything without
 * naming Markdown, or that ranks HTML at least as high, gets the HTML page,
 * which is why browsers with their default headers never receive Markdown.
 *
 * @param {string | null | undefined} acceptHeader The value of the request's
 * `Accept` header, or null when the request carries none.
 * @returns {boolean} True when the response should be the Markdown variant.
 */
export function prefersMarkdown(acceptHeader) {
    if (typeof acceptHeader !== "string" || acceptHeader.trim() === "") {
        return false;
    }

    const markdownQuality = qualityOf(acceptHeader, "text/markdown");
    if (markdownQuality === null || markdownQuality <= 0) {
        return false;
    }

    const htmlQuality = qualityOf(acceptHeader, "text/html");
    if (htmlQuality !== null) {
        return markdownQuality > htmlQuality;
    }

    return qualityOf(acceptHeader, "*/*") === null;
}

/**
 * Maps a documentation page path to its Markdown asset path.
 *
 * `/docs` and any path under `/docs/` without a file extension map to the
 * same path with `.md` appended, after a trailing slash is dropped. Every
 * other path returns null, including `.md` URLs themselves and pages outside
 * the documentation tree.
 *
 * @param {string} pathname The request path, such as `/docs/getting-started`.
 * @returns {string | null} The Markdown asset path, or null when the request
 * path has no Markdown variant.
 */
export function markdownPathFor(pathname) {
    if (typeof pathname !== "string") {
        return null;
    }

    let path = pathname;
    while (path.length > 1 && path.endsWith("/")) {
        path = path.slice(0, -1);
    }

    if (path !== "/docs" && !path.startsWith("/docs/")) {
        return null;
    }

    const lastSegment = path.slice(path.lastIndexOf("/") + 1);
    if (lastSegment.includes(".")) {
        return null;
    }

    return `${path}.md`;
}

/**
 * Reads the q-value of one media range in an `Accept` header.
 *
 * Media types compare case-insensitively, parameters other than `q` are
 * ignored, and a range without a `q` parameter counts as q=1. An unparseable
 * q-value also counts as q=1, matching the header's default.
 *
 * @param {string} acceptHeader The raw `Accept` header value.
 * @param {string} mediaType The lower-case media type or range to find.
 * @returns {number | null} The q-value clamped to 0-1, or null when the
 * header does not name the media type.
 */
function qualityOf(acceptHeader, mediaType) {
    for (const part of acceptHeader.split(",")) {
        const [range, ...parameters] = part.split(";");
        if (range.trim().toLowerCase() !== mediaType) {
            continue;
        }

        let quality = 1;
        for (const parameter of parameters) {
            const [name, value] = parameter.split("=");
            if (name.trim().toLowerCase() === "q" && value !== undefined) {
                const parsed = Number.parseFloat(value.trim());
                if (!Number.isNaN(parsed)) {
                    quality = Math.min(Math.max(parsed, 0), 1);
                }
            }
        }

        return quality;
    }

    return null;
}

/**
 * Reports whether a response carries an HTML body.
 *
 * @param {Response} response The static-asset response to inspect.
 * @returns {boolean} True when the response `Content-Type` is HTML.
 */
function isHtml(response) {
    const contentType = response.headers.get("content-type");
    return contentType !== null && contentType.toLowerCase().includes("text/html");
}

/**
 * Adds a field to the `Vary` header without duplicating it.
 *
 * @param {Headers} headers The mutable headers to update.
 * @param {string} field The field to add, such as `Accept`.
 * @returns {void}
 */
function addVary(headers, field) {
    const current = headers.get("vary");
    if (current === null || current.trim() === "") {
        headers.set("Vary", field);
        return;
    }

    const fields = current.split(",").map((entry) => entry.trim().toLowerCase());
    if (fields.includes(field.toLowerCase()) || fields.includes("*")) {
        return;
    }

    headers.set("Vary", `${current}, ${field}`);
}

/**
 * Builds the Markdown variant of a documentation page response.
 *
 * The body and caching headers come from the `.md` asset unchanged. The
 * `Content-Type` names Markdown explicitly, `Vary: Accept` keeps caches from
 * mixing the variants, `X-Robots-Tag: noindex` keeps search results pointing
 * at the HTML page, and the `Link` header names the HTML page canonical.
 *
 * @param {Response} markdownResponse The 200 response for the `.md` asset.
 * @param {string} canonicalUrl The absolute URL of the HTML page.
 * @returns {Response} The Markdown variant response.
 */
function markdownVariant(markdownResponse, canonicalUrl) {
    const headers = new Headers(markdownResponse.headers);
    headers.set("Content-Type", "text/markdown; charset=utf-8");
    headers.set("X-Robots-Tag", "noindex");
    headers.set("Link", `<${canonicalUrl}>; rel="canonical"`);
    addVary(headers, "Accept");
    return new Response(markdownResponse.body, {
        status: markdownResponse.status,
        statusText: markdownResponse.statusText,
        headers,
    });
}

/**
 * Returns a copy of an HTML response with `Vary: Accept` added.
 *
 * @param {Response} response The static-asset response to copy.
 * @returns {Response} The same response with the added `Vary` entry.
 */
function withVaryAccept(response) {
    const headers = new Headers(response.headers);
    addVary(headers, "Accept");
    return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers,
    });
}

export default {
    /**
     * Serves Markdown to clients that prefer it and static assets to others.
     *
     * For `GET` and `HEAD` requests to a documentation page whose `Accept`
     * header prefers Markdown, fetches the page's `.md` asset and answers
     * with the Markdown variant. A missing `.md` asset falls through to the
     * normal static-asset response. Every other request is served from static
     * assets unchanged, except that HTML responses under `/docs` gain
     * `Vary: Accept` so caches keep the two variants apart.
     *
     * @param {Request} request The incoming request.
     * @param {{ ASSETS: { fetch: typeof fetch } }} env The Worker bindings.
     * @returns {Promise<Response>} The HTML or Markdown response.
     */
    async fetch(request, env) {
        const url = new URL(request.url);
        const markdownPath = markdownPathFor(url.pathname);
        const method = request.method.toUpperCase();

        if (
            (method === "GET" || method === "HEAD") &&
            markdownPath !== null &&
            prefersMarkdown(request.headers.get("accept"))
        ) {
            const markdownUrl = new URL(markdownPath, url.origin).toString();
            const markdownResponse = await env.ASSETS.fetch(new Request(markdownUrl, request));
            if (markdownResponse.status === 200) {
                return markdownVariant(markdownResponse, url.origin + markdownPath.slice(0, -".md".length));
            }
        }

        const response = await env.ASSETS.fetch(request);
        if (markdownPath !== null && isHtml(response)) {
            return withVaryAccept(response);
        }

        return response;
    },
};
