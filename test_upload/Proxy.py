from mitmproxy import http, ctx
import os
import requests
import time
import urllib3
import threading
import queue
import shutil
from concurrent.futures import ThreadPoolExecutor
from requests.packages.urllib3.util.retry import Retry
from requests.adapters import HTTPAdapter

# ==================== 核心配置 ====================
VERSION = "9.0.0"
CACHE_FOLDER = "cache"
MAX_CONCURRENT_DOWNLOADS = 16  # 真正的最大并发任务数
CHUNK_SIZE = 5 * 1024 * 1024   # 5MB，触发分块下载的阈值
DOWNLOAD_CHUNK_SIZE = 5 * 1024 * 1024  # 5MB，网络请求每次读取的块大小

DOMAIN_TO_REGION = {
    'autopatchos.honkaiimpact3.com': 'os',
    'autopatchglb.honkaiimpact3.com': 'glb',
    'hi3-cdn-tw.heavens-era.com': 'tw'
}

# 仅保留真正需要同步优先下载的配置文件
CACHE_PATTERNS = [
    'ResourceVersion.unity3d', 'build.status', 'DataVersion.unity3d',
    'patchconfig.xmf', 'patchconfig', 'manifest_', 'Audio/',
    'StreamingAsb/', 'Video/', 'VideoEncrypt/', 'BlockMeta', 'manifest'
]

# ==================== 工具与全局 ====================
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
log_queue = queue.Queue()
downloading = set()
downloading_lock = threading.Lock()

# 真正的并发引擎：限制最大并发数为 16
GLOBAL_EXECUTOR = ThreadPoolExecutor(max_workers=MAX_CONCURRENT_DOWNLOADS)

thread_local = threading.local()

def get_session():
    """线程本地 Session，复用 TCP 连接"""
    if not hasattr(thread_local, "session"):
        session = requests.Session()
        retry = Retry(total=3, backoff_factor=1, status_forcelist=[500, 502, 503, 504, 429])
        adapter = HTTPAdapter(pool_connections=20, pool_maxsize=32, max_retries=retry)
        for prefix in ('http://', 'https://'):
            session.mount(prefix, adapter)
        session.trust_env = False
        session.proxies = {"http": None, "https": None}
        session.verify = False
        thread_local.session = session
    return thread_local.session

def cleanup_temp_directory():
    """
    清理所有缓存目录下的临时分块文件夹 (.chunks_*)
    带重试机制，防止因文件被占用导致删除失败
    """
    if not os.path.exists(CACHE_FOLDER):
        return
    
    max_retries = 3      # 最多重试3次
    retry_delay = 0.5    # 每次重试间隔0.5秒
    
    try:
        for region_dir in os.listdir(CACHE_FOLDER):
            full_region_path = os.path.join(CACHE_FOLDER, region_dir)
            if not os.path.isdir(full_region_path):
                continue
                
            for item in os.listdir(full_region_path):
                if item.startswith('.chunks_') and os.path.isdir(os.path.join(full_region_path, item)):
                    temp_dir = os.path.join(full_region_path, item)
                    ctx.log(f"🧹 清理残留: 发现临时目录 {temp_dir}")
                    
                    # --- 带重试的删除逻辑 ---
                    for attempt in range(max_retries):
                        try:
                            shutil.rmtree(temp_dir)
                            ctx.log(f"✅ 成功删除: {temp_dir}")
                            break  # 删除成功，跳出重试循环
                            
                        except (PermissionError, OSError) as e:
                            if attempt < max_retries - 1:
                                ctx.log(f"⚠️ 删除被拒绝 (重试 {attempt + 1}/{max_retries}): {temp_dir} - {e}")
                                time.sleep(retry_delay) # 等待后重试
                            else:
                                ctx.log(f"❌ 删除失败 (重试用尽): {temp_dir} - {e}")
                                
                        except Exception as e:
                            # 捕获其他未知异常，直接放弃重试
                            ctx.log(f"❌ 未知错误导致删除失败: {temp_dir} - {e}")
                            break
                            
    except Exception as e:
        ctx.log(f"❌ 遍历目录时发生错误: {e}")
# ==================== 下载核心 ====================
def download_file(task):
    """统一下载入口"""
    url, cache_path = task['url'], task['cache_path']
    filename = os.path.basename(cache_path)
    log_queue.put(f"🔽 开始: {filename}")
    
    session = get_session()
    try:
        head_resp = session.head(url, timeout=10)
        total_size = int(head_resp.headers.get('content-length', 0))
        
        if total_size >= CHUNK_SIZE:
            # 大文件分块下载逻辑
            num_chunks = min(MAX_CONCURRENT_DOWNLOADS, max(1, total_size // CHUNK_SIZE))
            temp_dir = os.path.join(os.path.dirname(cache_path), f".chunks_{filename}")
            os.makedirs(temp_dir, exist_ok=True)
            
            chunk_size_each = total_size // num_chunks
            
            def fetch_chunk(idx, start, end):
                chunk_path = os.path.join(temp_dir, f"chunk_{idx:03d}")
                resp = session.get(url, headers={'Range': f'bytes={start}-{end}'}, stream=True, timeout=(10, 60))
                resp.raise_for_status()
                with open(chunk_path, 'wb') as f:
                    for chunk in resp.iter_content(DOWNLOAD_CHUNK_SIZE):
                        f.write(chunk)
                return chunk_path

            # 使用线程池并发下载分块
            futures = []
            for i in range(num_chunks):
                start_byte = i * chunk_size_each
                end_byte = start_byte + chunk_size_each - 1 if i < num_chunks - 1 else total_size - 1
                futures.append(GLOBAL_EXECUTOR.submit(fetch_chunk, i, start_byte, end_byte))
            
            # 收集分块结果
            chunk_files = [f.result() for f in futures]
                
            # 合并分块
            with open(cache_path, 'wb') as out_f:
                for cf in chunk_files:
                    with open(cf, 'rb') as f: out_f.write(f.read())
                    os.remove(cf)
            if os.path.exists(temp_dir): os.rmdir(temp_dir)
        else:
            # 小文件直接流式下载
            resp = session.get(url, stream=True, timeout=(10, 60))
            resp.raise_for_status()
            os.makedirs(os.path.dirname(cache_path), exist_ok=True)
            with open(cache_path, 'wb') as f:
                for chunk in resp.iter_content(DOWNLOAD_CHUNK_SIZE):
                    f.write(chunk)
                    
        log_queue.put(f"✅ 完成: {filename}")
        return True
    except Exception as e:
        log_queue.put(f"❌ 失败: {filename} ({e})")
        if os.path.exists(cache_path): os.remove(cache_path)
        return False
    finally:
        # 下载完成后，从去重集合中移除
        with downloading_lock:
            downloading.discard(task['id'])

# ==================== mitmproxy 事件 ====================
def load(loader):
    ctx.options.connection_strategy = "lazy"
    ctx.options.upstream_cert = False
    ctx.options.ssl_insecure = True
    if not os.path.exists(CACHE_FOLDER): os.makedirs(CACHE_FOLDER)

    def log_processor():
        while True:
            try:
                msg = log_queue.get(timeout=1)
                if msg is None: break
                ctx.log(msg)
                log_queue.task_done()
            except queue.Empty: continue
    threading.Thread(target=log_processor, daemon=True).start()

def next_layer(nl):
    sni = nl.context.client.sni
    if sni and any(sni.endswith(x) for x in [
        "yuanshen.com", "mihoyo.com", "hoyoverse.com", "starrails.com", 
        "bhsr.com", "kurogame.com", "zenlesszonezero.com", 
        "honkaiimpact3.com", "bh3.com"
    ]):
        nl.context.server.address = ("127.0.0.1", 443)

def request(flow):
    host = flow.request.pretty_host
    if flow.request.host == '127.0.0.1' and flow.request.port == 8080:
        path = flow.request.path
        if 'overseas01' in path or 'com.miHoYo.bh3oversea' in path:
            host = 'autopatchos.honkaiimpact3.com'
        elif 'global' in path:
            host = 'autopatchglb.honkaiimpact3.com'

    if host not in DOMAIN_TO_REGION: return
    region = DOMAIN_TO_REGION[host]
    path = flow.request.path.split('?')[0]
    
    is_cache_pattern = any(p in path for p in CACHE_PATTERNS)
    cache_dir = os.path.join(CACHE_FOLDER, f"{region}_{VERSION}")
    cache_path = os.path.normpath(os.path.join(cache_dir, path.lstrip('/')))
    task_id = f"{host}{path}"
    url = f"https://{host}{flow.request.path}"

    # 1. 缓存命中
    if os.path.exists(cache_path):
        try:
            with open(cache_path, 'rb') as f:
                flow.response = http.Response.make(200, f.read(), {"Content-Type": "application/octet-stream"})
            ctx.log(f"✅ 缓存: {os.path.basename(cache_path)}")
            return
        except Exception as e:
            ctx.log(f"缓存读取失败: {e}")

    # 2. 下载去重
    with downloading_lock:
        if task_id in downloading:
            ctx.log(f"⏳ 等待: {os.path.basename(cache_path)}")
            return
        downloading.add(task_id)

    task = {'id': task_id, 'url': url, 'cache_path': cache_path}

    # 3. 逻辑分流
    if is_cache_pattern:
        # 配置文件：在当前请求线程同步下载，确保游戏启动不报错
        ctx.log(f"📦 配置文件: {os.path.basename(cache_path)}")
        try:
            success = download_file(task)
            if success:
                with open(cache_path, 'rb') as f:
                    flow.response = http.Response.make(200, f.read(), {"Content-Type": "application/octet-stream"})
                ctx.log(f"✅ 同步完成: {os.path.basename(cache_path)}")
            else:
                ctx.log(f"❌ 同步失败: {os.path.basename(cache_path)}")
        except Exception as e:
            ctx.log(f"❌ 同步异常: {e}")
    else:
        # 资源文件：直接提交到全局线程池，实现真正的 16 并发
        ctx.log(f"📥 资源文件: {os.path.basename(cache_path)}")
        GLOBAL_EXECUTOR.submit(download_file, task)
        # 对于异步下载，先返回一个占位响应或让游戏重试
        # 这里为了简单，直接返回 404 让客户端重新请求，或者返回空数据
        # 更好的做法是利用 mitmproxy 的拦截机制，但为了保持代码简洁，这里直接放行
        flow.response = http.Response.make(404, b"Downloading in background", {"Content-Type": "text/plain"})

def done():
    ctx.log("代理关闭，正在清理后台任务...")
    GLOBAL_EXECUTOR.shutdown(wait=True)
    
    # 在所有任务完成后，执行残留目录清理
    cleanup_temp_directory()
    
    ctx.log("所有任务已完成，代理已关闭")

addons = [next_layer, request, done]