/**
 * LazyBean Meta Ads Collector - Cloudflare Worker
 * 
 * Runs on Cloudflare Edge (Free plan, zero credit card, 100k req/day).
 * Queries public Meta Ad Library GraphQL API directly without credentials or proxies.
 * Captures bootstrap session cookies via response.headers.getSetCookie() and sends
 * them with GraphQL query matching meta-ads-collector requests.Session.
 */

const DOC_ID_SEARCH = "25464068859919530";
const CHROME_VERSION = "145";
const CHROME_FULL_VERSION = "145.0.7632.110";
const USER_AGENT = `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${CHROME_VERSION}.0.0.0 Safari/537.36`;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function timingSafeEqual(a, b) {
  if (typeof a !== "string" || typeof b !== "string") return false;
  const aTrim = a.trim();
  const bTrim = b.trim();
  if (aTrim.length === 0 || bTrim.length === 0) return false;
  if (aTrim.length !== bTrim.length) return false;

  let result = 0;
  for (let i = 0; i < aTrim.length; i++) {
    result |= aTrim.charCodeAt(i) ^ bTrim.charCodeAt(i);
  }
  return result === 0;
}

function isAuthorized(request, env) {
  const expectedSecret = env?.WORKER_SECRET;
  if (!expectedSecret || typeof expectedSecret !== "string" || expectedSecret.trim().length === 0) {
    return false;
  }

  const url = new URL(request.url);
  const keyParam = url.searchParams.get("key");
  const tokenParam = url.searchParams.get("token");
  const headerSecret = request.headers.get("X-Worker-Secret") || request.headers.get("X-Internal-Token");

  const candidate = keyParam || tokenParam || headerSecret;
  if (!candidate || typeof candidate !== "string") {
    return false;
  }

  return timingSafeEqual(expectedSecret, candidate);
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data, null, 2), {
    status,
    headers: {
      "Content-Type": "application/json",
      "Access-Control-Allow-Origin": "*",
    },
  });
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    // CORS preflight
    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, X-Worker-Secret, X-Internal-Token",
        },
      });
    }

    // Health check (No secret required) - returns only secretConfigured status
    if (url.pathname === "/health" || url.pathname === "/") {
      const isConfigured = Boolean(
        env?.WORKER_SECRET &&
        typeof env.WORKER_SECRET === "string" &&
        env.WORKER_SECRET.trim().length > 0
      );
      return jsonResponse({
        secretConfigured: isConfigured,
      });
    }

    // All other endpoints require authentication
    if (!isAuthorized(request, env)) {
      return jsonResponse({
        error: "Unauthorized",
        message: "Invalid or missing secret. Pass ?key=YOUR_SECRET or header 'X-Worker-Secret'.",
      }, 401);
    }

    // Diagnostics endpoint: Feasibility test for CAFELAX or any page ID
    if (url.pathname === "/diagnose") {
      const pageId = url.searchParams.get("pageId") || "100456846237420";
      const country = (url.searchParams.get("country") || "EG").toUpperCase();
      const result = await probeMetaAdLibrary(pageId, country);
      return jsonResponse(result);
    }

    // Collection endpoint
    if (url.pathname === "/api/ads/collect" && request.method === "POST") {
      try {
        const body = await request.json();
        const pageId = body.pageId;
        const country = (body.country || "EG").toUpperCase();
        const maxResults = body.maxResults || 50;

        if (!pageId) {
          return jsonResponse({ error: "pageId is required" }, 400);
        }

        const res = await fetchMetaAds(pageId, country, maxResults);
        return jsonResponse(res);
      } catch (err) {
        return jsonResponse({
          status: "Failed",
          error: err.message,
          stack: err.stack,
        }, 500);
      }
    }

    return jsonResponse({ error: "Not found" }, 404);
  },
};

function calculateJazoest(lsd) {
  if (!lsd) return "2893";
  let total = 0;
  for (let i = 0; i < lsd.length; i++) {
    total += lsd.charCodeAt(i);
  }
  return (2 + total).toString();
}

function extractCookies(response) {
  const cookieMap = new Map();
  // Seed default viewport cookies (meta-ads-collector requests.Session convention)
  cookieMap.set("wd", "1920x1080");
  cookieMap.set("dpr", "1");

  let rawSetCookies = [];
  // Cloudflare Workers & Fetch standard getSetCookie() to avoid header merging
  if (typeof response.headers.getSetCookie === "function") {
    rawSetCookies = response.headers.getSetCookie();
  } else if (response.headers.raw && typeof response.headers.raw === "function") {
    const raw = response.headers.raw();
    rawSetCookies = raw["set-cookie"] || [];
  } else {
    const single = response.headers.get("set-cookie");
    if (single) {
      rawSetCookies = single.split(/,\s*(?=[A-Za-z0-9_-]+=)/);
    }
  }

  for (const header of rawSetCookies) {
    if (!header) continue;
    const cookiePart = header.split(";")[0].trim();
    const eqIdx = cookiePart.indexOf("=");
    if (eqIdx > 0) {
      const name = cookiePart.slice(0, eqIdx).trim();
      const val = cookiePart.slice(eqIdx + 1).trim();
      if (name) {
        cookieMap.set(name, val);
      }
    }
  }

  return cookieMap;
}

async function executeAttempt(pageId, country, attemptNumber) {
  const t0 = Date.now();
  const attempt = {
    attempt: attemptNumber,
    timestamp: new Date().toISOString(),
    bootstrapStatus: 0,
    cookiesCount: 0,
    cookieKeys: [],
    tokensFound: {},
    challengeTriggered: false,
    interRequestDelayMs: 0,
    graphqlStatus: 0,
    status: "Unknown",
    errorCode: null,
    errorMessage: null,
    adsCount: 0,
    sampleAds: [],
    durationMs: 0,
  };

  try {
    // 1. Bootstrap session from facebook.com (logged out)
    const bootstrapHeaders = {
      "accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
      "accept-language": "en-US,en;q=0.9",
      "sec-ch-ua": `"Google Chrome";v="${CHROME_VERSION}", "Chromium";v="${CHROME_VERSION}", "Not_A Brand";v="24"`,
      "sec-ch-ua-mobile": "?0",
      "sec-ch-ua-platform": `"Windows"`,
      "sec-fetch-dest": "document",
      "sec-fetch-mode": "navigate",
      "sec-fetch-site": "none",
      "sec-fetch-user": "?1",
      "upgrade-insecure-requests": "1",
      "user-agent": USER_AGENT,
    };

    const bootResp = await fetch("https://www.facebook.com/", {
      headers: bootstrapHeaders,
      redirect: "follow",
    });

    attempt.bootstrapStatus = bootResp.status;
    const bootHtml = await bootResp.text();

    // Check challenge or checkpoint in HTML
    if (bootHtml.includes("checkpoint") || bootHtml.includes("challenge") || bootHtml.includes("captcha")) {
      attempt.challengeTriggered = true;
    }

    // Capture all Set-Cookie values (datr, fr, sb, etc.) via getSetCookie()
    const cookieMap = extractCookies(bootResp);
    attempt.cookiesCount = cookieMap.size;
    attempt.cookieKeys = Array.from(cookieMap.keys());

    // Extract tokens from HTML
    let lsd = null;
    const lsdMatch = bootHtml.match(/"LSD",\[\],\{"token":"([^"]+)"\}/)
      || bootHtml.match(/\["LSD",\[\],\{"token":"([^"]+)"/)
      || bootHtml.match(/"lsd":"([^"]+)"/)
      || bootHtml.match(/name="lsd" value="([^"]+)"/);
    if (lsdMatch) lsd = lsdMatch[1];

    let spinR = "1033837939";
    const revMatch = bootHtml.match(/"__spin_r":(\d+)/)
      || bootHtml.match(/"server_revision":(\d+)/)
      || bootHtml.match(/"revision":(\d+)/);
    if (revMatch) spinR = revMatch[1];

    let spinT = Math.floor(Date.now() / 1000).toString();
    const spinTMatch = bootHtml.match(/"__spin_t":(\d+)/);
    if (spinTMatch) spinT = spinTMatch[1];

    let hsi = (Date.now() * 1000).toString();
    const hsiMatch = bootHtml.match(/"__hsi":"(\d+)"/) || bootHtml.match(/"hsi":"(\d+)"/);
    if (hsiMatch) hsi = hsiMatch[1];

    attempt.tokensFound = {
      lsd: !!lsd,
      spinR: !!spinR,
      spinT: !!spinT,
      hsi: !!hsi,
    };

    if (!lsd) {
      attempt.status = "Failed";
      attempt.errorMessage = "Could not extract LSD token from Facebook homepage";
      attempt.durationMs = Date.now() - t0;
      return attempt;
    }

    // 2. Wait 2-4 seconds between bootstrap and GraphQL (jittered delay)
    const delayMs = 2000 + Math.floor(Math.random() * 2000);
    attempt.interRequestDelayMs = delayMs;
    await sleep(delayMs);

    // 3. Perform GraphQL search request
    const jazoest = calculateJazoest(lsd);
    const sessionId = crypto.randomUUID();
    const collationToken = crypto.randomUUID();

    const variables = {
      activeStatus: "ALL",
      adType: "ALL",
      bylines: [],
      collationToken,
      contentLanguages: [],
      countries: [country],
      excludedIDs: [],
      first: 30,
      isTargetedCountry: false,
      location: null,
      mediaType: "ALL",
      multiCountryFilterMode: null,
      pageIDs: [pageId],
      potentialReachInput: [],
      publisherPlatforms: [],
      queryString: "",
      regions: [],
      searchType: "PAGE",
      sessionID: sessionId,
      source: null,
      startDate: null,
      v: "fbece7",
      viewAllPageID: "0",
    };

    const formParams = new URLSearchParams();
    formParams.set("av", "0");
    formParams.set("__aaid", "0");
    formParams.set("__user", "0");
    formParams.set("__a", "1");
    formParams.set("__req", "1");
    formParams.set("__hs", "20476.HYP:comet_plat_default_pkg.2.1...0");
    formParams.set("dpr", "1");
    formParams.set("__ccg", "GOOD");
    formParams.set("__rev", spinR);
    formParams.set("__hsi", hsi);
    formParams.set("__comet_req", "94");
    formParams.set("lsd", lsd);
    formParams.set("jazoest", jazoest);
    formParams.set("__spin_r", spinR);
    formParams.set("__spin_b", "trunk");
    formParams.set("__spin_t", spinT);
    formParams.set("__jssesw", "1");
    formParams.set("fb_api_caller_class", "RelayModern");
    formParams.set("fb_api_req_friendly_name", "AdLibrarySearchPaginationQuery");
    formParams.set("server_timestamps", "true");
    formParams.set("variables", JSON.stringify(variables));
    formParams.set("doc_id", DOC_ID_SEARCH);

    const cookieHeader = Array.from(cookieMap.entries())
      .map(([k, v]) => `${k}=${v}`)
      .join("; ");

    const gqlHeaders = {
      "accept": "*/*",
      "accept-language": "en-US,en;q=0.9",
      "content-type": "application/x-www-form-urlencoded",
      "origin": "https://www.facebook.com",
      "referer": `https://www.facebook.com/ads/library/?active_status=all&ad_type=all&country=${country}&view_all_page_id=${pageId}&search_type=page&media_type=all`,
      "sec-ch-ua": `"Google Chrome";v="${CHROME_VERSION}", "Chromium";v="${CHROME_VERSION}", "Not_A Brand";v="24"`,
      "sec-ch-ua-mobile": "?0",
      "sec-ch-ua-platform": `"Windows"`,
      "sec-fetch-dest": "empty",
      "sec-fetch-mode": "cors",
      "sec-fetch-site": "same-origin",
      "user-agent": USER_AGENT,
      "x-asbd-id": "359341",
      "x-fb-lsd": lsd,
      "cookie": cookieHeader,
    };

    const gqlResp = await fetch("https://www.facebook.com/api/graphql/", {
      method: "POST",
      headers: gqlHeaders,
      body: formParams.toString(),
    });

    attempt.graphqlStatus = gqlResp.status;
    const gqlText = await gqlResp.text();

    if (gqlResp.status === 403) {
      attempt.status = "Blocked";
      attempt.errorMessage = "Facebook returned HTTP 403 to Cloudflare Worker IP";
      attempt.durationMs = Date.now() - t0;
      return attempt;
    }

    let parsed;
    try {
      parsed = JSON.parse(gqlText);
    } catch {
      attempt.status = "Failed";
      attempt.errorMessage = "Invalid JSON response from Facebook: " + gqlText.slice(0, 300);
      attempt.durationMs = Date.now() - t0;
      return attempt;
    }

    if (parsed.errors && parsed.errors.length > 0) {
      const err = parsed.errors[0];
      const code = err.code || (err.summary ? err.summary : null);
      attempt.errorCode = code;
      attempt.errorMessage = err.message || err.summary || "GraphQL error";
      if (code === 1675004 || (err.message && err.message.toLowerCase().includes("rate limit"))) {
        attempt.status = "RateLimited";
      } else {
        attempt.status = "Failed";
      }
      attempt.durationMs = Date.now() - t0;
      return attempt;
    }

    const edges = parsed?.data?.ad_library_main?.search_results_connection?.edges
      || parsed?.data?.ad_library_main?.search_results?.edges
      || [];

    attempt.adsCount = edges.length;
    attempt.status = "Success";

    for (const edge of edges.slice(0, 5)) {
      const node = edge.node || edge;
      const collated = node.collated_results?.[0] || node;
      attempt.sampleAds.push({
        id: collated.ad_archive_id || collated.id,
        startDate: collated.start_date,
        isActive: collated.is_active,
        title: collated.snapshot?.title,
      });
    }

    attempt.durationMs = Date.now() - t0;
    return attempt;
  } catch (err) {
    attempt.status = "Failed";
    attempt.errorMessage = err.message;
    attempt.durationMs = Date.now() - t0;
    return attempt;
  }
}

async function probeMetaAdLibrary(pageId, country = "EG") {
  const startTime = Date.now();
  const attempts = [];

  // Attempt 1
  const attempt1 = await executeAttempt(pageId, country, 1);
  attempts.push(attempt1);

  // On 1675004 Rate limit: wait 30s, fresh bootstrap session, retry once
  if (attempt1.status === "RateLimited" || attempt1.errorCode === 1675004) {
    attempt1.retryAction = "Rate limit 1675004 encountered. Waiting 30 seconds before fresh bootstrap...";
    await sleep(30000);
    const attempt2 = await executeAttempt(pageId, country, 2);
    attempts.push(attempt2);
  }

  const finalAttempt = attempts[attempts.length - 1];

  return {
    targetPageId: pageId,
    country,
    timestamp: new Date().toISOString(),
    status: finalAttempt.status,
    totalAttempts: attempts.length,
    attempts,
    adsCount: finalAttempt.adsCount,
    sampleAds: finalAttempt.sampleAds,
    rawError: finalAttempt.errorMessage,
    durationMs: Date.now() - startTime,
  };
}

async function fetchMetaAds(pageId, country = "EG", maxResults = 50) {
  const probe = await probeMetaAdLibrary(pageId, country);
  if (probe.status !== "Success") {
    return {
      status: probe.status,
      pageId,
      country,
      totalCollected: 0,
      ads: [],
      error: probe.rawError,
      attempts: probe.attempts,
    };
  }

  return {
    status: "Success",
    pageId,
    country,
    totalCollected: probe.adsCount,
    ads: probe.sampleAds,
    error: null,
    attempts: probe.attempts,
  };
}
