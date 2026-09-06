import hashlib
import json
import os
import urllib.error
import urllib.request
from urllib.parse import parse_qsl, urlencode, urlparse, urlunparse

from mitmproxy import http
from mitmproxy import ctx
from mitmproxy.proxy import layer


PRIMARY_CDN_HOST = "prod-zspnsalicdn.kurogame.com"
SECONDARY_CDN_HOST = "prod-zspnstxcdn.kurogame.com"
LOCAL_RESOURCE_HOST = "127.0.0.1"
LOCAL_RESOURCE_PORT = 8081
CDN_SCHEME = "http"


def load(loader):
    # ctx.options.web_open_browser = False
    # Change connection strategy to lazy so next_layer
    # happens before actually connecting upstream.
    ctx.options.connection_strategy = "lazy"
    # Disable upstream certificate verification/pinning behavior.
    ctx.options.upstream_cert = False
    ctx.options.ssl_insecure = True
    # Hosts that should not be intercepted by mitmproxy.
    ctx.options.ignore_hosts = [
        r".*sdk-prod-cdn-aws\.kurogame-service\.(com|xyz).*",
        r".*qcloud-sg-datareceiver\.kurogame\.xyz.*",
        r".*mp-gb-sdklog\.kurogames\.net.*",
        r".*events\.appsflyer\.com.*",
        r"pgr\.kurogame\.net:443",
    ]
    _ensure_cache_root()


def _normalise_connect_host(host):
    if host in {None, "", "*", "0.0.0.0", "::", "[::]"}:
        return "127.0.0.1"
    return host


def _is_local_wildcard_host(host):
    return host in {"*", "0.0.0.0", "::", "[::]"}


def _ascnet_target():
    raw_target = os.environ.get(
        "ASCNET_PROXY_TARGET",
        "http://127.0.0.1:8080"
    ).strip()
    if "://" not in raw_target:
        raw_target = f"http://{raw_target}"
    parsed = urlparse(raw_target)
    scheme = parsed.scheme or "http"
    host = _normalise_connect_host(parsed.hostname)
    port = parsed.port or (443 if scheme == "https" else 80)
    return scheme, host, port


def _flow_log_path():
    return os.environ.get("ASCNET_PROXY_LOG")


def _redact_url(url):
    parsed = urlparse(url)
    query = []
    for key, value in parse_qsl(
        parsed.query,
        keep_blank_values=True
    ):
        if key.lower() in {
            "token",
            "access_token",
            "accesstoken",
            "refresh_token",
            "code",
            "password",
            "pwd",
        }:
            value = "<redacted>"
        query.append((key, value))
    return urlunparse(
        parsed._replace(
            query=urlencode(query)
        )
    )


def _log_flow(prefix, flow):
    path = _flow_log_path()
    if not path:
        return
    status = (
        getattr(flow.response, "status_code", "-")
        if getattr(flow, "response", None)
        else "-"
    )
    line = (
        f"{prefix} "
        f"{flow.request.method} "
        f"{_redact_url(flow.request.pretty_url)} "
        f"-> {status}\n"
    )
    with open(path, "a", encoding="utf-8") as handle:
        handle.write(line)


# -------------------------------------------------------------
# CDN cache
# -------------------------------------------------------------

def _cache_root():
    return os.environ.get(
        "ASCNET_CDN_CACHE",
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "proxy_cache")
    )


def _ensure_cache_root():
    os.makedirs(_cache_root(), exist_ok=True)


def _cdn_cache_key(host, path, query):
    """Return a stable cache directory + filename.

    The URL path is preserved as directories so cached files are easy to
    inspect. Query strings are represented by a short SHA-256 suffix.
    """
    clean_path = path.split("?", 1)[0]
    clean_path = clean_path.replace("\\", "/")
    parts = [p for p in clean_path.split("/") if p not in {"", "."}]

    # Never allow '..' to escape the cache root.
    parts = [p for p in parts if p != ".."]
    if not parts:
        parts = ["index"]

    directory = os.path.join(_cache_root(), host, *parts[:-1])
    filename = parts[-1]

    if query:
        digest = hashlib.sha256(query.encode("utf-8")).hexdigest()[:16]
        filename = f"{filename}.__q_{digest}"

    data_path = os.path.join(directory, filename)
    meta_path = data_path + ".json"
    return data_path, meta_path


def _cache_headers(response):
    """Keep only response headers useful for replaying a cached resource."""
    headers = {}
    for key, value in response.headers.items(multi=True):
        lower = key.lower()
        if lower in {
            "content-type",
            "content-encoding",
            "content-language",
            "cache-control",
            "etag",
            "last-modified",
            "expires",
            "accept-ranges",
        }:
            headers[key] = value
    return headers


def _cache_save(host, path, query, response):
    if response is None:
        return

    # Only cache complete successful GET responses. A 206 response is a
    # partial object and must not replace the complete cached resource.
    if response.status_code != 200:
        return

    if response.raw_content is None:
        return

    data_path, meta_path = _cdn_cache_key(host, path, query)
    os.makedirs(os.path.dirname(data_path), exist_ok=True)

    tmp_data = data_path + ".tmp"
    tmp_meta = meta_path + ".tmp"

    try:
        with open(tmp_data, "wb") as handle:
            handle.write(response.raw_content)

        metadata = {
            "host": host,
            "path": path,
            "query": query,
            "status_code": response.status_code,
            "headers": _cache_headers(response),
            "size": len(response.raw_content),
        }
        with open(tmp_meta, "w", encoding="utf-8") as handle:
            json.dump(metadata, handle, ensure_ascii=False, indent=2)

        os.replace(tmp_data, data_path)
        os.replace(tmp_meta, meta_path)
        ctx.log.info(
            f"[CDN-CACHE] SAVED {host}{path} -> {data_path}"
        )
    except Exception as exc:
        for temp in (tmp_data, tmp_meta):
            try:
                if os.path.exists(temp):
                    os.remove(temp)
            except OSError:
                pass
        ctx.log.warn(f"[CDN-CACHE] SAVE FAILED {host}{path}: {exc}")


def _cache_load(host, path, query):
    data_path, meta_path = _cdn_cache_key(host, path, query)
    if not os.path.isfile(data_path) or not os.path.isfile(meta_path):
        return None

    try:
        with open(meta_path, "r", encoding="utf-8") as handle:
            metadata = json.load(handle)
        with open(data_path, "rb") as handle:
            body = handle.read()

        headers = metadata.get("headers") or {}
        headers["Content-Length"] = str(len(body))

        return http.Response.make(
            int(metadata.get("status_code", 200)),
            body,
            headers,
        )
    except Exception as exc:
        ctx.log.warn(
            f"[CDN-CACHE] READ FAILED {host}{path}: {exc}"
        )
        return None



def _secondary_fallback_url(path, query):
    url = f"{CDN_SCHEME}://{SECONDARY_CDN_HOST}{path}"
    if query:
        url += "?" + query
    return url


def _fetch_secondary_fallback(flow, path, query):
    """Fetch the same resource from Secondary when Primary returns 404.

    This is intentionally limited to GET requests. The client still sees the
    original Primary/local URL; only the proxy performs the fallback fetch.
    Successful Secondary responses are stored in the Secondary cache, so later
    requests can be served completely offline.
    """
    if flow.request.method != "GET":
        return None

    url = _secondary_fallback_url(path, query)
    headers = {
        "User-Agent": flow.request.headers.get("User-Agent", ""),
        "Accept": flow.request.headers.get("Accept", "*/*"),
        "Accept-Encoding": "identity",
    }

    # Preserve a small set of headers that can affect CDN responses, but do
    # not forward Host/connection-specific headers into urllib.
    for name in ("Referer", "Origin"):
        value = flow.request.headers.get(name)
        if value:
            headers[name] = value

    request = urllib.request.Request(
        url,
        headers=headers,
        method="GET",
    )

    try:
        # Do not inherit Windows/system HTTP(S)_PROXY settings here.
        # This fallback is a direct CDN-to-proxy fetch; inheriting a proxy
        # can cause a 20-second timeout even though the CDN is reachable.
        opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({})
        )
        with opener.open(request, timeout=8) as upstream:
            status = getattr(upstream, "status", 200)
            body = upstream.read()
            response_headers = {}
            for key in (
                "Content-Type",
                "Content-Language",
                "Cache-Control",
                "ETag",
                "Last-Modified",
                "Expires",
                "Accept-Ranges",
            ):
                value = upstream.headers.get(key)
                if value is not None:
                    response_headers[key] = value
            response_headers["Content-Length"] = str(len(body))

            fallback_response = http.Response.make(
                status,
                body,
                response_headers,
            )

            if status == 200:
                _cache_save(
                    SECONDARY_CDN_HOST,
                    path,
                    query,
                    fallback_response,
                )
                ctx.log.info(
                    f"[CDN-CACHE] FALLBACK OK {SECONDARY_CDN_HOST}{path}"
                )
            else:
                ctx.log.info(
                    f"[CDN-CACHE] FALLBACK STATUS {SECONDARY_CDN_HOST}{path} "
                    f"status={status}"
                )

            return fallback_response

    except urllib.error.HTTPError as exc:
        # urllib raises HTTPError for 4xx/5xx. We only need a small response
        # object so the caller can keep the original Primary error when the
        # Secondary also fails.
        ctx.log.info(
            f"[CDN-CACHE] FALLBACK FAILED {SECONDARY_CDN_HOST}{path} "
            f"status={exc.code}"
        )
        return http.Response.make(
            exc.code,
            b"",
            {"Content-Length": "0"},
        )
    except Exception as exc:
        ctx.log.info(
            f"[CDN-CACHE] FALLBACK ERROR {SECONDARY_CDN_HOST}{path}: {exc}"
        )
        return None

def _is_primary_local_resource_request(flow):
    host = flow.request.pretty_host
    port = flow.request.port
    path = flow.request.path.split("?", 1)[0]
    return (
        host == LOCAL_RESOURCE_HOST
        and port == LOCAL_RESOURCE_PORT
        and path.startswith("/prod/")
        and flow.request.method in {"GET", "HEAD"}
    )


def _cdn_host_for_request(flow):
    host = (flow.request.pretty_host or "").lower()
    if host == PRIMARY_CDN_HOST:
        return PRIMARY_CDN_HOST
    if host == SECONDARY_CDN_HOST:
        return SECONDARY_CDN_HOST
    return None


def _request_query_string(flow):
    return flow.request.pretty_url.split("?", 1)[1] if "?" in flow.request.pretty_url else ""


def _serve_cached_cdn_request(flow, cdn_host):
    path = flow.request.path.split("?", 1)[0]
    query = _request_query_string(flow)
    cached = _cache_load(cdn_host, path, query)
    if cached is None:
        ctx.log.info(
            f"[CDN-CACHE] MISS {cdn_host}{path}"
        )
        return False

    if flow.request.method == "HEAD":
        cached.content = b""
        cached.headers["Content-Length"] = "0"

    flow.response = cached
    ctx.log.info(
        f"[CDN-CACHE] HIT {cdn_host}{path}"
    )
    _log_flow("CDN-CACHE-HIT", flow)
    return True


def _rewrite_local_primary_to_upstream(flow):
    original_host = flow.request.host
    original_scheme = flow.request.scheme
    flow.request.scheme = CDN_SCHEME
    flow.request.host = PRIMARY_CDN_HOST
    flow.request.port = 80
    flow.request.headers["Host"] = PRIMARY_CDN_HOST
    flow.request.headers["X-Forwarded-Host"] = original_host
    flow.request.headers["X-Forwarded-Proto"] = original_scheme
    flow.metadata["ascnet_cdn_cache_host"] = PRIMARY_CDN_HOST
    ctx.log.info(
        f"[CDN-CACHE] MISS -> DOWNLOAD {PRIMARY_CDN_HOST}{flow.request.path}"
    )


def _prepare_secondary_cdn_request(flow):
    """Mark a real Secondary CDN request so response() can cache it."""
    flow.metadata["ascnet_cdn_cache_host"] = SECONDARY_CDN_HOST
    ctx.log.info(
        f"[CDN-CACHE] MISS -> DOWNLOAD {SECONDARY_CDN_HOST}{flow.request.path}"
    )


def _is_ascnet_host(host):
    if not host:
        return False
    if host in {
        "sdkapi.kurogame-service.com",
        "sdkapi.kurogame-service.xyz",
        "prod-zspnslog.zspms-game.com",
        "api.ipify.org",
        "icanhazip.com",
        "dc.services.visualstudio.com",
    }:
        return True
    if (
        host.startswith(("prod-encdn-", "prod-twcdn-"))
        and host.endswith(".kurogame.net")
    ):
        return True
    if (
        host.startswith((
            "prod-zspns-txcdn",
            "prod-zspnsalicdn",
            "prod-zspnstxcdn",
        ))
        and host.endswith(".kurogame.com")
    ):
        return True
    if host.endswith(".pgr-game.com") and (
        host.startswith("prod-twcdn-")
        or host.startswith("prod-encdn-")
        or host.startswith("prod-pay-")
    ):
        return True
    return False


def _is_pgr_game_popup_notice_request(flow):
    host = flow.request.pretty_host
    path = flow.request.path.split("?", 1)[0]
    return (
        host
        and host.startswith(("prod-encdn-", "prod-twcdn-"))
        and host.endswith(".pgr-game.com")
        and path.startswith("/prod/client/notice/config/")
        and path.endswith("/PopUpPicNotice.json")
    )


def _is_upstream_notice_html_request(flow):
    path = flow.request.path.split("?", 1)[0]
    return (
        _is_ascnet_host(flow.request.pretty_host)
        and path.startswith("/prod/client/notice/html/")
    )


def _is_ascnet_gate_request(flow):
    return (
        flow.request.path.split("?", 1)[0]
        == "/api/Login/Login"
    )


def _is_feedback_request(flow):
    return (
        flow.request.pretty_host in {
            "prod-zspnslog.zspms-game.com",
            "prod.enzspnslog.kurogame.com",
            "prod.twzspnslog.kurogame.com",
        }
        and flow.request.path.split("?", 1)[0]
        == "/feedback"
    )


def _is_ip_check_request(flow):
    """
    External IP check endpoints.
    dc.services.visualstudio.com is intentionally NOT included here.
    It has its own 204 handler below.
    """
    return (
        flow.request.pretty_host in {
            "icanhazip.com",
            "api.ipify.org",
        }
        and flow.request.path.split("?", 1)[0] in {
            "",
            "/",
        }
    )


def _is_visualstudio_request(flow):
    """
    Microsoft Visual Studio / Application Insights endpoint.
    The game may receive a 404 from the real endpoint.
    We intentionally sink these requests with HTTP 204.
    """
    return (
        flow.request.pretty_host
        == "dc.services.visualstudio.com"
    )


def _is_wildcard_connect_request(flow):
    return (
        flow.request.method == "CONNECT"
        and _is_local_wildcard_host(
            flow.request.pretty_host
        )
    )


def _is_wildcard_ascnet_request(flow):
    path = flow.request.path.split("?", 1)[0]
    return (
        _is_local_wildcard_host(
            flow.request.pretty_host
        )
        and path.startswith((
            "/api/",
            "/prod/",
            "/sdkcom/",
        ))
    )


def next_layer(nextlayer: layer.NextLayer):
    # Only log hosts that we intend to rewrite.
    #
    # HTTPS proxying is intentionally avoided for pinned
    # KRSDK/service hosts by the runner/environment.
    sni = nextlayer.context.client.sni
    if sni and _is_ascnet_host(sni):
        ctx.log.info(
            "ascnet candidate sni:" + sni
        )


def http_connect(flow: http.HTTPFlow) -> None:
    _log_flow("CONNECT", flow)
    if not _is_wildcard_connect_request(flow):
        return
    flow.response = http.Response.make(
        502,
        (
            b"AscNet blocked invalid CONNECT target "
            b"0.0.0.0/::; restart with run_steam.py "
            b"so local SDK URLs use 127.0.0.1.\n"
        ),
        {
            "Content-Type": "text/plain",
        },
    )
    _log_flow("CONNECT-BLOCK", flow)


def request(flow: http.HTTPFlow) -> None:
    path = flow.request.path.split("?", 1)[0]

    # ---------------------------------------------------------
    # Local Primary CDN resource cache
    #
    # ConfigController points PrimaryCdns at:
    # http://127.0.0.1:8081/prod
    #
    # Cache HIT: serve locally without Internet.
    # Cache MISS: rewrite to the real Primary CDN and let
    # response() save the complete 200 response.
    # ---------------------------------------------------------
    if _is_primary_local_resource_request(flow):
        # 1) Normal Primary cache.
        if _serve_cached_cdn_request(flow, PRIMARY_CDN_HOST):
            return

        # 2) If Primary cache is missing, try the Secondary cache before
        #    touching the Internet. If found, promote that object into the
        #    Primary cache so the local Primary URL becomes self-contained.
        path = flow.request.path.split("?", 1)[0]
        query = _request_query_string(flow)
        secondary_cached = _cache_load(
            SECONDARY_CDN_HOST,
            path,
            query,
        )
        if secondary_cached is not None:
            if flow.request.method == "HEAD":
                secondary_cached.content = b""
                secondary_cached.headers["Content-Length"] = "0"
            flow.response = secondary_cached
            ctx.log.info(
                f"[CDN-CACHE] SECONDARY CACHE HIT -> PRIMARY {path}"
            )

            # Promote the cached Secondary object to the Primary namespace.
            # This makes subsequent local Primary requests independent of
            # both CDNs and therefore suitable for offline operation.
            if flow.request.method == "GET":
                _cache_save(
                    PRIMARY_CDN_HOST,
                    path,
                    query,
                    secondary_cached,
                )
            _log_flow("CDN-CACHE-SECONDARY-HIT", flow)
            return

        # 3) No local copy exists. Download from real Primary. response()
        #    will automatically try Secondary if Primary returns 404.
        _rewrite_local_primary_to_upstream(flow)
        return

    # ---------------------------------------------------------
    # Real Secondary CDN
    #
    # Keep the public Secondary CDN URL intact so the game's
    # normal CDN failover remains unchanged. If the request has
    # already been cached, serve it without contacting Internet.
    # On a miss, mark it for response() so the downloaded file is
    # persisted for future offline runs.
    # ---------------------------------------------------------
    if (
        flow.request.pretty_host == SECONDARY_CDN_HOST
        and path.startswith("/prod/")
        and flow.request.method in {"GET", "HEAD"}
    ):
        if _serve_cached_cdn_request(flow, SECONDARY_CDN_HOST):
            return
        _prepare_secondary_cdn_request(flow)
        return

    # ---------------------------------------------------------
    # Local AscNet API
    # ---------------------------------------------------------
    if path in (
        "/api/AscNet/register",
        "/api/AscNet/login",
        "/api/AscNet/verify",
    ):
        flow.request.scheme = "http"
        flow.request.host = "127.0.0.1"
        flow.request.port = 8080
        return

    _log_flow("REQ", flow)

    # ---------------------------------------------------------
    # Microsoft Visual Studio / Application Insights
    #
    # Fix:
    # dc.services.visualstudio.com -> 204 No Content
    #
    # This is intentionally handled before the AscNet
    # upstream rewrite logic.
    # ---------------------------------------------------------
    if _is_visualstudio_request(flow):
        flow.response = http.Response.make(
            204,
            b"",
            {
                "Content-Length": "0",
            },
        )
        _log_flow(
            "VISUALSTUDIO-204",
            flow
        )
        return

    # ---------------------------------------------------------
    # Feedback endpoint
    # ---------------------------------------------------------
    if _is_feedback_request(flow):
        flow.response = http.Response.make(
            204,
            b"",
            {
                "Content-Type": "text/plain",
                "Content-Length": "0",
            },
        )
        _log_flow(
            "SINK",
            flow
        )
        return

    # ---------------------------------------------------------
    # External IP check
    # ---------------------------------------------------------
    if _is_ip_check_request(flow):
        body = b"127.0.0.1\n"
        flow.response = http.Response.make(
            200,
            body,
            {
                "Content-Type": "text/plain; charset=utf-8",
                "Content-Length": str(len(body)),
            },
        )
        _log_flow(
            "IP-CHECK",
            flow
        )
        return

    # ---------------------------------------------------------
    # Notice HTML
    #
    # Notice metadata points at version-specific CDN HTML
    # files. Keep those requests on the original CDN so
    # new notices work without local fixtures.
    # ---------------------------------------------------------
    if _is_upstream_notice_html_request(flow):
        _log_flow(
            "PASS",
            flow
        )
        return

    # ---------------------------------------------------------
    # AscNet request filtering
    # ---------------------------------------------------------
    if not (
        _is_ascnet_host(
            flow.request.pretty_host
        )
        or _is_pgr_game_popup_notice_request(flow)
        or _is_ascnet_gate_request(flow)
        or _is_wildcard_ascnet_request(flow)
    ):
        return

    # ---------------------------------------------------------
    # Rewrite request to local AscNet server
    # ---------------------------------------------------------
    scheme, host, port = _ascnet_target()
    original_host = flow.request.host
    original_scheme = flow.request.scheme
    flow.request.scheme = scheme
    flow.request.host = host
    flow.request.port = port
    flow.request.headers["Host"] = (
        host
        if port in (80, 443)
        else f"{host}:{port}"
    )
    flow.request.headers["X-Forwarded-Host"] = (
        original_host
    )
    flow.request.headers["X-Forwarded-Proto"] = (
        original_scheme
    )


def response(flow: http.HTTPFlow) -> None:
    cache_host = flow.metadata.get("ascnet_cdn_cache_host")

    if cache_host and flow.request.method == "GET" and flow.response is not None:
        path = flow.request.path.split("?", 1)[0]
        query = _request_query_string(flow)

        # -----------------------------------------------------
        # Primary 404 -> Secondary fallback
        #
        # The client requested the local Primary URL. If the real
        # Primary CDN does not contain the object, try Secondary
        # without changing ConfigController or the client's CDN
        # configuration. A successful Secondary response is cached
        # under the Secondary cache namespace.
        # -----------------------------------------------------
        if (
            cache_host == PRIMARY_CDN_HOST
            and flow.response.status_code == 404
        ):
            ctx.log.info(
                f"[CDN-CACHE] PRIMARY 404 -> SECONDARY {path}"
            )
            fallback = _fetch_secondary_fallback(
                flow,
                path,
                query,
            )
            if fallback is not None and fallback.status_code == 200:
                flow.response = fallback
                flow.metadata["ascnet_cdn_recovered_from_secondary"] = True

                # Keep the Secondary copy AND promote the same bytes into
                # Primary cache. The client continues to see the Primary URL,
                # while the next request can be served without any network.
                _cache_save(
                    PRIMARY_CDN_HOST,
                    path,
                    query,
                    fallback,
                )
                ctx.log.info(
                    f"[CDN-CACHE] PRIMARY RECOVERED {path} "
                    f"via {SECONDARY_CDN_HOST}"
                )
            else:
                ctx.log.info(
                    f"[CDN-CACHE] PRIMARY 404 CONFIRMED {path}"
                )

        # Save successful Primary responses and real Secondary responses.
        # If Primary was recovered from Secondary above, it is already saved
        # in the Secondary cache and should not be duplicated into Primary.
        if flow.response.status_code == 200:
            if cache_host == PRIMARY_CDN_HOST and flow.metadata.get(
                "ascnet_cdn_recovered_from_secondary"
            ):
                pass
            else:
                _cache_save(
                    cache_host,
                    path,
                    query,
                    flow.response,
                )
            ctx.log.info(
                f"[CDN-CACHE] READY {cache_host}{path}"
            )
        else:
            ctx.log.info(
                f"[CDN-CACHE] NOT-CACHED {cache_host}{path} "
                f"status={flow.response.status_code}"
            )

    _log_flow(
        "RSP",
        flow
    )
