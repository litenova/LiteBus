// Sends every request for www.litebus.io to the same path and query on litebus.io,
// so the site has one address. A permanent redirect lets browsers and search engines learn it.
export default {
  fetch(request) {
    const url = new URL(request.url);
    url.hostname = "litebus.io";
    url.protocol = "https:";
    url.port = "";
    return Response.redirect(url.toString(), 301);
  },
};
