"""
Magazine Search API - Production-ready hybrid search service with distributed rate limiting,
circuit breakers, caching, and OpenTelemetry observability.
"""
import asyncio
import hashlib
import json
import logging
import os
import random
import sys
import zlib
from collections import OrderedDict
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from functools import lru_cache
from time import time
from typing import Any, Dict, List, Literal, Optional, Tuple

from dotenv import load_dotenv
from elasticsearch import AsyncElasticsearch, exceptions as es_exceptions
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.middleware.gzip import GZipMiddleware
from fastapi.responses import JSONResponse
from opentelemetry import metrics, trace
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.elasticsearch import ElasticsearchInstrumentor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
from opentelemetry.propagate import set_global_textmap
from opentelemetry.propagators.b3 import B3MultiFormat
from opentelemetry.propagators.composite import CompositePropagator
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import (
    DEPLOYMENT_ENVIRONMENT,
    SERVICE_NAME,
    SERVICE_VERSION,
    Resource,
)
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor, ConsoleSpanExporter
from opentelemetry.trace.propagation.tracecontext import TraceContextTextMapPropagator
from pydantic import BaseModel, Field
from sentence_transformers import SentenceTransformer

load_dotenv()

# =============================================================================
# Configuration
# =============================================================================

class Config:
    """Application configuration from environment variables."""
    
    # Elasticsearch
    ES_HOST: str = os.getenv("ES_HOST", "localhost")
    ES_PORT: int = int(os.getenv("ES_PORT", "9200"))
    ES_USER: Optional[str] = os.getenv("ES_USER")
    ES_PASS: Optional[str] = os.getenv("ES_PASS")
    ES_MAX_CONNS: int = int(os.getenv("ES_MAX_CONNS", "50"))
    ES_TIMEOUT: int = int(os.getenv("ES_TIMEOUT", "30"))
    ES_SEARCH_TIMEOUT: int = int(os.getenv("ES_SEARCH_TIMEOUT", "5"))
    
    # Redis
    REDIS_URL: Optional[str] = os.getenv("REDIS_URL")
    REDIS_MAX_CONNS: int = int(os.getenv("REDIS_MAX_CONNS", "10"))
    
    # Model
    MODEL_NAME: str = os.getenv("MODEL_NAME", "all-MiniLM-L6-v2")
    CACHE_SIZE: int = int(os.getenv("CACHE_SIZE", "10000"))
    CACHE_TTL: int = int(os.getenv("CACHE_TTL", "300"))
    MAX_CONCURRENT: int = int(os.getenv("MAX_CONCURRENT", "50"))
    BATCH_SIZE: int = int(os.getenv("BATCH_SIZE", "32"))
    EMBEDDING_CACHE_SIZE: int = int(os.getenv("EMBEDDING_CACHE_SIZE", "2000"))
    
    # API
    WORKERS: int = int(os.getenv("WORKERS", "4"))
    DEBUG: bool = os.getenv("DEBUG", "false").lower() == "true"
    API_KEYS: List[str] = [k.strip() for k in os.getenv("API_KEYS", "").split(",") if k.strip()]
    
    # Rate limiting
    RATE_LIMIT_REQUESTS: int = int(os.getenv("RATE_LIMIT_REQUESTS", "100"))
    RATE_LIMIT_PERIOD: int = int(os.getenv("RATE_LIMIT_PERIOD", "60"))
    
    # Circuit breaker
    CIRCUIT_ERROR_RATE_THRESHOLD: float = float(os.getenv("CIRCUIT_ERROR_RATE_THRESHOLD", "0.1"))
    CIRCUIT_MINIMUM_REQUESTS: int = int(os.getenv("CIRCUIT_MINIMUM_REQUESTS", "10"))
    CIRCUIT_RECOVERY_TIMEOUT: int = int(os.getenv("CIRCUIT_RECOVERY_TIMEOUT", "30"))
    CIRCUIT_SLIDING_WINDOW: int = int(os.getenv("CIRCUIT_SLIDING_WINDOW", "60"))
    
    # Retry
    RETRY_MAX_ATTEMPTS: int = int(os.getenv("RETRY_MAX_ATTEMPTS", "3"))
    RETRY_BASE_DELAY: float = float(os.getenv("RETRY_BASE_DELAY", "1.0"))
    RETRY_MAX_DELAY: float = float(os.getenv("RETRY_MAX_DELAY", "10.0"))
    
    # Observability
    OTEL_ENABLED: bool = os.getenv("OTEL_ENABLED", "false").lower() == "true"
    OTEL_ENDPOINT: Optional[str] = os.getenv("OTEL_ENDPOINT")
    OTEL_SERVICE_NAME: str = os.getenv("OTEL_SERVICE_NAME", "magazine-search")
    OTEL_ENVIRONMENT: str = os.getenv("OTEL_ENVIRONMENT", "production")
    
    # Cache warming
    WARM_QUERIES: List[str] = [
        q.strip() for q in os.getenv("WARM_QUERIES", "technology,science,business,health,design,programming,ai,cloud").split(",")
        if q.strip()
    ]
    WARM_CACHE_SIZE: int = int(os.getenv("WARM_CACHE_SIZE", "100"))

config = Config()

# =============================================================================
# Logging
# =============================================================================

class JSONFormatter(logging.Formatter):
    """JSON formatter with trace ID support."""
    
    def format(self, record: logging.LogRecord) -> str:
        log_data: Dict[str, Any] = {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "level": record.levelname,
            "name": record.name,
            "message": record.getMessage(),
            "module": record.module,
            "function": record.funcName,
            "line": record.lineno
        }
        
        if hasattr(record, 'trace_id'):
            log_data["trace_id"] = record.trace_id
        if hasattr(record, 'span_id'):
            log_data["span_id"] = record.span_id
        if hasattr(record, 'extra'):
            log_data.update(record.extra)
        if record.exc_info:
            log_data["exception"] = self.formatException(record.exc_info)
        
        return json.dumps(log_data)

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

if not config.DEBUG:
    handler = logging.StreamHandler()
    handler.setFormatter(JSONFormatter())
    logger.handlers = [handler]

# =============================================================================
# OpenTelemetry
# =============================================================================

def setup_telemetry() -> Tuple[Optional[TracerProvider], Optional[MeterProvider]]:
    """Initialize OpenTelemetry tracing and metrics.
    
    Returns:
        Tuple of (tracer_provider, meter_provider)
    """
    if not config.OTEL_ENABLED:
        return None, None
    
    resource = Resource.create({
        SERVICE_NAME: config.OTEL_SERVICE_NAME,
        SERVICE_VERSION: "2.0.0",
        DEPLOYMENT_ENVIRONMENT: config.OTEL_ENVIRONMENT,
    })
    
    tracer_provider = TracerProvider(resource=resource)
    meter_provider: Optional[MeterProvider] = None
    if config.OTEL_ENDPOINT:
        span_exporter = OTLPSpanExporter(endpoint=config.OTEL_ENDPOINT, insecure=True)
        tracer_provider.add_span_processor(BatchSpanProcessor(span_exporter))
    
    if config.DEBUG:
        tracer_provider.add_span_processor(BatchSpanProcessor(ConsoleSpanExporter()))
    
    trace.set_tracer_provider(tracer_provider)
    
    set_global_textmap(CompositePropagator([
        TraceContextTextMapPropagator(),
        B3MultiFormat(),
    ]))
    
    if config.OTEL_ENDPOINT:
        metric_reader = PeriodicExportingMetricReader(
            OTLPMetricExporter(endpoint=config.OTEL_ENDPOINT, insecure=True),
            export_interval_millis=10000
        )
        meter_provider = MeterProvider(resource=resource, metric_readers=[metric_reader])
        metrics.set_meter_provider(meter_provider)
    
    try:
        ElasticsearchInstrumentor().instrument()
    except Exception as e:
        logger.warning(f"Failed to instrument Elasticsearch: {e}")
    
    logger.info(f"OpenTelemetry configured: endpoint={config.OTEL_ENDPOINT}")
    return tracer_provider, meter_provider

# =============================================================================
# Rate Limiter
# =============================================================================

class SlidingWindowRateLimiter:
    """Local sliding window rate limiter using ring buffer."""
    
    def __init__(self, requests_per_period: int = 100, period_seconds: int = 60) -> None:
        self.requests_per_period = requests_per_period
        self.period_seconds = period_seconds
        self.buckets: Dict[str, Any] = {}
        self._lock = asyncio.Lock()
    
    async def is_allowed(self, key: str) -> bool:
        """Check if request is allowed using sliding window algorithm."""
        async with self._lock:
            return self._is_allowed_unlocked(key)

    def _is_allowed_unlocked(self, key: str) -> bool:
        """Check the local sliding window while holding the lock."""
        now = time()
        
        if key not in self.buckets:
            self.buckets[key] = [0.0] * self.requests_per_period
            self.buckets[key + "_idx"] = 0
            self.buckets[key + "_count"] = 0
        
        idx = self.buckets[key + "_idx"]
        count = self.buckets[key + "_count"]
        timestamps = self.buckets[key]
        
        if count >= self.requests_per_period:
            cutoff = now - self.period_seconds
            valid_count = 0
            for i in range(self.requests_per_period):
                if timestamps[(idx + i) % self.requests_per_period] > cutoff:
                    valid_count += 1
                else:
                    timestamps[(idx + i) % self.requests_per_period] = 0.0
            
            if valid_count >= self.requests_per_period:
                return False
            
            count = valid_count
            while timestamps[idx] <= cutoff and timestamps[idx] != 0:
                idx = (idx + 1) % self.requests_per_period
        
        self.buckets[key][(idx + count) % self.requests_per_period] = now
        self.buckets[key + "_count"] = count + 1
        return True

class RedisRateLimiter:
    """Distributed sliding window rate limiter using Redis."""
    
    def __init__(self, redis_url: Optional[str] = None) -> None:
        self.redis_url = redis_url
        self.redis = None
        self._local = SlidingWindowRateLimiter(
            config.RATE_LIMIT_REQUESTS,
            config.RATE_LIMIT_PERIOD
        )
    
    async def initialize(self) -> None:
        """Initialize Redis connection."""
        if self.redis_url:
            try:
                import redis.asyncio as redis
                self.redis = redis.from_url(
                    self.redis_url,
                    max_connections=config.REDIS_MAX_CONNS,
                    decode_responses=True
                )
                await self.redis.ping()
                logger.info(f"Redis connected at {self.redis_url}")
            except Exception as e:
                logger.warning(f"Redis connection failed, using local rate limiter: {e}")
                self.redis = None
        else:
            logger.info("No Redis URL provided, using local rate limiter")
    
    async def is_allowed(self, key: str) -> bool:
        """Check rate limit using Redis Lua script for atomic sliding window."""
        if not self.redis:
            return await self._local.is_allowed(key)
        
        try:
            lua_script = """
            local key = KEYS[1]
            local now = tonumber(ARGV[1])
            local period = tonumber(ARGV[2])
            local limit = tonumber(ARGV[3])
            
            redis.call('ZREMRANGEBYSCORE', key, 0, now - period)
            local count = redis.call('ZCARD', key)
            
            if count < limit then
                redis.call('ZADD', key, now, now .. ':' .. math.random())
                redis.call('EXPIRE', key, period)
                return 1
            else
                return 0
            end
            """
            
            result = await self.redis.eval(
                lua_script,
                1,
                f"ratelimit:{key}",
                time(),
                config.RATE_LIMIT_PERIOD,
                config.RATE_LIMIT_REQUESTS
            )
            return bool(result)
        except Exception as e:
            logger.warning(f"Redis rate limit error, falling back to local: {e}")
            return await self._local.is_allowed(key)
    
    async def close(self) -> None:
        """Close Redis connection."""
        if self.redis:
            await self.redis.close()
            logger.info("Redis connection closed")

# =============================================================================
# Circuit Breaker
# =============================================================================

class CircuitState(Enum):
    """Circuit breaker states."""
    CLOSED = "CLOSED"
    OPEN = "OPEN"
    HALF_OPEN = "HALF_OPEN"

@dataclass
class CircuitWindow:
    """Sliding window for error rate calculation."""
    requests: List[float] = field(default_factory=list)
    errors: List[float] = field(default_factory=list)
    
    def add_request(self, timestamp: float) -> None:
        self.requests.append(timestamp)
    
    def add_error(self, timestamp: float) -> None:
        self.errors.append(timestamp)
    
    def prune(self, window_seconds: int) -> None:
        cutoff = time() - window_seconds
        self.requests = [t for t in self.requests if t > cutoff]
        self.errors = [t for t in self.errors if t > cutoff]
    
    def error_rate(self) -> float:
        if not self.requests:
            return 0.0
        return len(self.errors) / len(self.requests)

class ErrorRateCircuitBreaker:
    """Circuit breaker based on error rate in sliding window."""
    
    def __init__(
        self,
        error_rate_threshold: float = 0.1,
        minimum_requests: int = 10,
        recovery_timeout: int = 30,
        sliding_window: int = 60
    ) -> None:
        self.error_rate_threshold = error_rate_threshold
        self.minimum_requests = minimum_requests
        self.recovery_timeout = recovery_timeout
        self.sliding_window = sliding_window
        self.state = CircuitState.CLOSED
        self.window = CircuitWindow()
        self.last_failure_time = 0.0
        self._lock = asyncio.Lock()
    
    def _should_open(self) -> bool:
        return (
            len(self.window.requests) >= self.minimum_requests
            and self.window.error_rate() >= self.error_rate_threshold
        )

    @staticmethod
    def _is_transient_failure(exc: Exception) -> bool:
        if isinstance(
            exc,
            (
                es_exceptions.ConnectionError,
                es_exceptions.ConnectionTimeout,
            ),
        ):
            return True

        return (
            isinstance(exc, es_exceptions.ApiError)
            and getattr(exc, "status_code", None) in {429, 502, 503, 504}
        )

    async def call(self, func, *args, **kwargs):
        """Execute a dependency call with circuit-breaker protection."""
        async with self._lock:
            now = time()
            self.window.prune(self.sliding_window)

            if self.state == CircuitState.OPEN:
                if now - self.last_failure_time >= self.recovery_timeout:
                    self.state = CircuitState.HALF_OPEN
                    logger.info("Circuit breaker is HALF_OPEN")
                else:
                    raise HTTPException(503, "Service temporarily unavailable")

        try:
            result = await func(*args, **kwargs)
        except Exception as exc:
            if not self._is_transient_failure(exc):
                raise

            async with self._lock:
                now = time()
                self.window.add_request(now)
                self.window.add_error(now)
                self.last_failure_time = now

                if self.state == CircuitState.HALF_OPEN or self._should_open():
                    self.state = CircuitState.OPEN
                    logger.error(
                        "Circuit breaker is OPEN: error_rate=%.2f%%",
                        self.window.error_rate() * 100,
                    )
            raise

        async with self._lock:
            now = time()
            self.window.add_request(now)

            if self.state == CircuitState.HALF_OPEN:
                self.state = CircuitState.CLOSED
                self.window = CircuitWindow()
                logger.info("Circuit breaker is CLOSED")

        return result

# =============================================================================
# Retry
# =============================================================================

class RetryWithBackoff:
    """Retry with exponential backoff for transient failures."""
    
    @staticmethod
    async def call(func, *args, **kwargs):
        """Execute function with retry logic."""
        last_exception = None
        
        for attempt in range(config.RETRY_MAX_ATTEMPTS):
            try:
                return await func(*args, **kwargs)
            except (es_exceptions.ConnectionError, es_exceptions.ConnectionTimeout) as e:
                last_exception = e
                if attempt < config.RETRY_MAX_ATTEMPTS - 1:
                    delay = min(
                        config.RETRY_BASE_DELAY * (2 ** attempt) + random.uniform(0, 0.1),
                        config.RETRY_MAX_DELAY
                    )
                    logger.warning(f"Retry {attempt + 1}/{config.RETRY_MAX_ATTEMPTS} after {delay:.2f}s: {e}")
                    await asyncio.sleep(delay)
            except Exception:
                raise
        
        if last_exception is not None:
            raise last_exception
        raise RuntimeError("Retry loop exited without a result or exception")

# =============================================================================
# Cache
# =============================================================================

class SlidingTTLCache:
    """LRU cache with sliding TTL and compression."""
    
    def __init__(self, max_size: int = 10000, ttl_seconds: int = 300) -> None:
        self.cache = OrderedDict()
        self.max_size = max_size
        self.ttl = ttl_seconds
        self.hits = 0
        self.misses = 0
        self._lock = asyncio.Lock()
    
    async def get(self, key: str) -> Optional[Dict]:
        """Get cached value with sliding TTL refresh on hit."""
        async with self._lock:
            if key in self.cache:
                compressed_data, timestamp = self.cache[key]
                if (time() - timestamp) < self.ttl:
                    self.cache[key] = (compressed_data, time())
                    self.cache.move_to_end(key)
                    self.hits += 1
                    return json.loads(zlib.decompress(compressed_data).decode())
                del self.cache[key]
            self.misses += 1
            return None
    
    async def set(self, key: str, value: Dict) -> None:
        """Set cached value with compression."""
        async with self._lock:
            compressed = zlib.compress(json.dumps(value).encode(), level=3)
            if len(self.cache) >= self.max_size:
                self.cache.popitem(last=False)
            self.cache[key] = (compressed, time())
    
    async def warm(self, items: List[Tuple[str, Dict]]) -> None:
        """Warm cache with multiple items."""
        for key, value in items:
            compressed = zlib.compress(json.dumps(value).encode(), level=3)
            self.cache[key] = (compressed, time())

# =============================================================================
# Embedding Batcher
# =============================================================================

class EmbeddingBatcher:
    """Batch embeddings with request coalescing and caching."""
    
    def __init__(self, model: SentenceTransformer, batch_size: int = 32) -> None:
        self.model = model
        self.batch_size = batch_size
        self._cache: OrderedDict[str, List[float]] = OrderedDict()
        self._cache_lock = asyncio.Lock()
        self._inflight: Dict[str, asyncio.Future] = {}
        self._inflight_lock = asyncio.Lock()
    
    async def _process_batch(self, batch: List[str]) -> List[List[float]]:
        """Process a batch synchronously in thread pool."""
        return await asyncio.to_thread(
            lambda: self.model.encode(
                batch,
                batch_size=self.batch_size,
                show_progress_bar=False,
            ).tolist()
        )
    
    async def get_embeddings(self, texts: List[str]) -> List[List[float]]:
        """Get embeddings with request coalescing and batching."""
        if not texts:
            return []
        
        results: List[Optional[List[float]]] = []
        to_encode: List[str] = []
        
        async with self._cache_lock:
            for text in texts:
                if text in self._cache:
                    self._cache.move_to_end(text)
                    results.append(self._cache[text])
                else:
                    results.append(None)
                    to_encode.append(text)
        
        if not to_encode:
            return results  # type: ignore
        
        futures: List[asyncio.Future] = []
        texts_to_embed: List[str] = []
        
        async with self._inflight_lock:
            for text in to_encode:
                if text in self._inflight:
                    futures.append(self._inflight[text])
                else:
                    future = asyncio.Future()
                    self._inflight[text] = future
                    futures.append(future)
                    texts_to_embed.append(text)
        
        if texts_to_embed:
            try:
                embeddings = []
                for i in range(0, len(texts_to_embed), self.batch_size):
                    batch = texts_to_embed[i:i + self.batch_size]
                    batch_embeddings = await self._process_batch(batch)
                    embeddings.extend(batch_embeddings)
                
                async with self._cache_lock:
                    for text, emb in zip(texts_to_embed, embeddings):
                        self._cache[text] = emb
                        if len(self._cache) > config.EMBEDDING_CACHE_SIZE:
                            to_remove = len(self._cache) - int(config.EMBEDDING_CACHE_SIZE * 0.8)
                            for _ in range(to_remove):
                                self._cache.popitem(last=False)
                
                async with self._inflight_lock:
                    for text, emb in zip(texts_to_embed, embeddings):
                        if text in self._inflight and not self._inflight[text].done():
                            self._inflight[text].set_result(emb)
                        del self._inflight[text]
            except Exception as e:
                async with self._inflight_lock:
                    for text in texts_to_embed:
                        if text in self._inflight and not self._inflight[text].done():
                            self._inflight[text].set_exception(e)
                        del self._inflight[text]
                raise
        
        await asyncio.gather(*futures)
        
        async with self._cache_lock:
            return [self._cache[text] for text in texts]

# =============================================================================
# Health Checker
# =============================================================================

class DependencyHealthChecker:
    """Check health of all dependencies."""
    
    def __init__(self, app_state: Any) -> None:
        self.state = app_state
    
    async def check_all(self) -> Dict[str, Dict]:
        """Check all dependencies."""
        results = {}
        results["elasticsearch"] = await self._check_elasticsearch()
        
        if self.state.rate_limiter.redis:
            results["redis"] = await self._check_redis()
        
        results["model"] = await self._check_model()
        return results
    
    async def _check_elasticsearch(self) -> Dict:
        start = time()
        try:
            info = await asyncio.wait_for(self.state.es.info(), timeout=5.0)
            latency = (time() - start) * 1000
            cluster_health = await asyncio.wait_for(self.state.es.cluster.health(), timeout=5.0)
            
            return {
                "healthy": True,
                "latency_ms": latency,
                "version": info.get("version", {}).get("number", "unknown"),
                "cluster_status": cluster_health.get("status", "unknown"),
                "nodes": cluster_health.get("number_of_nodes", 0)
            }
        except Exception as e:
            return {
                "healthy": False,
                "latency_ms": (time() - start) * 1000,
                "error": str(e)
            }
    
    async def _check_redis(self) -> Dict:
        start = time()
        try:
            await asyncio.wait_for(self.state.rate_limiter.redis.ping(), timeout=2.0)
            return {
                "healthy": True,
                "latency_ms": (time() - start) * 1000
            }
        except Exception as e:
            return {
                "healthy": False,
                "latency_ms": (time() - start) * 1000,
                "error": str(e)
            }
    
    async def _check_model(self) -> Dict:
        try:
            await self.state.embedder.get_embeddings(["test"])
            return {"healthy": True, "model": config.MODEL_NAME}
        except Exception as e:
            return {"healthy": False, "error": str(e)}

# =============================================================================
# Search Functions
# =============================================================================

async def search_keyword(
    es: AsyncElasticsearch,
    q: str,
    size: int,
    from_: int,
    category: Optional[str] = None
) -> Dict:
    """Execute keyword search with retry."""
    query = {
        "size": size,
        "from": from_,
        "query": {
            "bool": {
                "must": [{
                    "multi_match": {
                        "query": q,
                        "fields": ["title^3", "author^2", "content"],
                        "fuzziness": "AUTO"
                    }
                }]
            }
        },
        "_source": ["id", "title", "author", "content", "category", "updated_at"]
    }
    
    if category:
        query["query"]["bool"]["filter"] = [{"term": {"category.keyword": category}}]
    
    async def _search():
        return await es.search(index="articles", body=query)
    
    return await RetryWithBackoff.call(_search)

async def search_vector(
    es: AsyncElasticsearch,
    embedder: EmbeddingBatcher,
    q: str,
    size: int,
    from_: int,
    category: Optional[str] = None
) -> Dict:
    """Execute vector similarity search with retry."""
    vector = (await embedder.get_embeddings([q]))[0]
    
    query = {
        "size": size,
        "from": from_,
        "query": {
            "script_score": {
                "query": {"match_all": {}},
                "script": {
                    "source": "cosineSimilarity(params.vec, 'embedding') + 1.0",
                    "params": {"vec": vector}
                }
            }
        },
        "_source": ["id", "title", "author", "content", "category", "updated_at"]
    }
    
    if category:
        query["query"]["script_score"]["query"] = {
            "bool": {
                "must": {"match_all": {}},
                "filter": [{"term": {"category.keyword": category}}]
            }
        }
    
    async def _search():
        return await es.search(index="articles_vector", body=query)
    
    return await RetryWithBackoff.call(_search)

async def search_hybrid(
    es: AsyncElasticsearch,
    embedder: EmbeddingBatcher,
    q: str,
    size: int,
    from_: int,
    category: Optional[str] = None
) -> Dict:
    """Execute hybrid search combining keyword and vector results."""
    async def _search():
        keyword, vector = await asyncio.gather(
            search_keyword(es, q, size * 2, from_, category),
            search_vector(es, embedder, q, size * 2, from_, category),
            return_exceptions=True
        )
        
        if isinstance(keyword, Exception):
            logger.warning(f"Keyword search failed: {keyword}")
            keyword = {"hits": {"hits": []}}
            kw_weight = 0.0
        else:
            kw_weight = 0.6
        
        if isinstance(vector, Exception):
            logger.warning(f"Vector search failed: {vector}")
            vector = {"hits": {"hits": []}}
            vec_weight = 0.0
        else:
            vec_weight = 0.4
        
        if kw_weight == 0.0 and vec_weight == 0.0:
            return {"hits": {"hits": []}, "took": 0}
        
        if kw_weight == 0.0:
            vec_weight = 1.0
        elif vec_weight == 0.0:
            kw_weight = 1.0
        
        combined = {}
        max_kw = max([h["_score"] for h in keyword["hits"]["hits"]], default=1.0)
        max_vec = max([h["_score"] for h in vector["hits"]["hits"]], default=1.0)
        
        for hit in keyword["hits"]["hits"]:
            combined[hit["_id"]] = {
                "hit": hit,
                "score": (hit["_score"] / max_kw) * kw_weight
            }
        
        for hit in vector["hits"]["hits"]:
            if hit["_id"] in combined:
                combined[hit["_id"]]["score"] += (hit["_score"] / max_vec) * vec_weight
            else:
                combined[hit["_id"]] = {
                    "hit": hit,
                    "score": (hit["_score"] / max_vec) * vec_weight
                }
        
        sorted_hits = sorted(combined.values(), key=lambda x: x["score"], reverse=True)
        return {
            "hits": {"hits": [h["hit"] for h in sorted_hits[:size]]},
            "took": max(keyword.get("took", 0), vector.get("took", 0))
        }
    
    return await RetryWithBackoff.call(_search)

async def search_internal(
    fastapi_app,  # ← FIXED: Changed from 'app: FastAPI' to avoid naming conflict
    q: str,
    size: int,
    from_: int,
    category: Optional[str],
    search_type: str
) -> Dict:
    """Internal search router with circuit breaker."""
    if search_type == "auto":
        normalized_q = q.strip().lower()
        words = normalized_q.split()
        if len(words) <= 2:
            search_type = "keyword"
        elif (
            len(words) > 5
            or normalized_q.endswith("?")
            or normalized_q.startswith(("what", "how", "why"))
        ):
            search_type = "vector"
        else:
            search_type = "hybrid"
    
    cb = fastapi_app.state.circuit_breaker  # ← FIXED: Using fastapi_app
    
    if search_type == "keyword":
        return await cb.call(search_keyword, fastapi_app.state.es, q, size, from_, category)  # ← FIXED
    elif search_type == "vector":
        return await cb.call(search_vector, fastapi_app.state.es, fastapi_app.state.embedder, q, size, from_, category)  # ← FIXED
    else:
        return await cb.call(search_hybrid, fastapi_app.state.es, fastapi_app.state.embedder, q, size, from_, category)  # ← FIXED

# =============================================================================
# Pydantic Models
# =============================================================================

class SearchRequest(BaseModel):
    """Search request model."""
    q: str = Field(..., min_length=2, description="Search query")
    size: int = Field(default=10, ge=1, le=100, description="Number of results")
    from_: int = Field(default=0, ge=0, alias="from", description="Pagination offset")
    category: Optional[str] = Field(None, description="Filter by category")
    search_type: Literal["auto", "keyword", "vector", "hybrid"] = Field(
        default="auto",
        description="Search type",
    )
    
    model_config = {"populate_by_name": True}

# =============================================================================
# FastAPI Application
# =============================================================================

# Fix for Python 3.11+ event loop
if sys.version_info >= (3, 11):
    asyncio.set_event_loop_policy(asyncio.DefaultEventLoopPolicy())

@asynccontextmanager
async def lifespan(application: FastAPI):
    """Initialize and cleanup application resources."""
    logger.info("Starting up...")
    start_time = time()
    
    # Setup OpenTelemetry
    tracer_provider, meter_provider = setup_telemetry()
    application.state.tracer = trace.get_tracer("search-api")
    
    # Initialize Elasticsearch
    es_config = [f"http://{config.ES_HOST}:{config.ES_PORT}"]
    if config.ES_USER and config.ES_PASS:
        es_config = {
            "hosts": [f"http://{config.ES_HOST}:{config.ES_PORT}"],
            "basic_auth": (config.ES_USER, config.ES_PASS)
        }
    
    application.state.es = AsyncElasticsearch(
        **(es_config if isinstance(es_config, dict) else {"hosts": es_config}),
        request_timeout=config.ES_TIMEOUT,
        max_retries=config.RETRY_MAX_ATTEMPTS,
        retry_on_timeout=True,
    )
        
    # Initialize Redis rate limiter
    application.state.rate_limiter = RedisRateLimiter(config.REDIS_URL)
    await application.state.rate_limiter.initialize()
    
    # Load embedding model
    logger.info(f"Loading model: {config.MODEL_NAME}")
    model = await asyncio.to_thread(SentenceTransformer, config.MODEL_NAME)
    application.state.embedder = EmbeddingBatcher(model, config.BATCH_SIZE)
    logger.info("Model loaded")
    
    # Initialize caches and circuit breaker
    application.state.cache = SlidingTTLCache(config.CACHE_SIZE, config.CACHE_TTL)
    application.state.circuit_breaker = ErrorRateCircuitBreaker(
        error_rate_threshold=config.CIRCUIT_ERROR_RATE_THRESHOLD,
        minimum_requests=config.CIRCUIT_MINIMUM_REQUESTS,
        recovery_timeout=config.CIRCUIT_RECOVERY_TIMEOUT,
        sliding_window=config.CIRCUIT_SLIDING_WINDOW
    )
    application.state.start_time = time()
    application.state.semaphore = asyncio.Semaphore(config.MAX_CONCURRENT)
    application.state.inflight_cache = {}
    application.state.inflight_lock = asyncio.Lock()
    application.state.health_checker = DependencyHealthChecker(application.state)
    
    # Warm cache
    logger.info("Warming cache with popular queries...")
    warmed = 0
    for query in config.WARM_QUERIES:
        try:
            cache_key = hashlib.md5(f"{query}:10:0:None:hybrid".encode()).hexdigest()
            result = await search_internal(application, query, 10, 0, None, "hybrid")
            
            if result and result.get("hits", {}).get("hits"):
                hits = []
                for hit in result.get("hits", {}).get("hits", [])[:config.WARM_CACHE_SIZE]:
                    src = hit.get("_source", {})
                    hits.append({
                        "id": hit["_id"],
                        "title": src.get("title", ""),
                        "content": src.get("content", "")[:200],
                        "score": round(hit["_score"], 3),
                        "category": src.get("category", ""),
                        "updated_at": src.get("updated_at", "")
                    })
                
                response = {
                    "results": hits,
                    "total": result.get("hits", {}).get("total", {}).get("value", 0),
                    "search_type": "hybrid",
                    "took_ms": result.get("took", 0),
                    "cached": True
                }
                await application.state.cache.set(cache_key, response)
                warmed += 1
                logger.info(f"Warmed query: {query}")
        except Exception as e:
            logger.warning(f"Failed to warm query {query}: {e}")
    
    logger.info(f"Cache warmed with {warmed} queries")
    logger.info(f"Ready! Startup took {(time() - start_time):.2f}s")
    
    yield
    
    # Shutdown
    logger.info("Shutting down...")
    
    async with application.state.inflight_lock:
        if application.state.inflight_cache:
            logger.info(f"Waiting for {len(application.state.inflight_cache)} in-flight requests...")
            await asyncio.gather(*[
                event.wait() for event in application.state.inflight_cache.values()
            ], return_exceptions=True)
    
    await application.state.es.close()
    await application.state.rate_limiter.close()
    logger.info("Shutdown complete")

app = FastAPI(
    lifespan=lifespan,
    title="Magazine Search API",
    version="2.0.0",
    docs_url="/docs" if config.DEBUG else None,
    redoc_url="/redoc" if config.DEBUG else None
)

# Instrument with OpenTelemetry
if config.OTEL_ENABLED:
    FastAPIInstrumentor.instrument_app(app)
    HTTPXClientInstrumentor().instrument()

app.add_middleware(GZipMiddleware, minimum_size=1000)

# =============================================================================
# Middleware
# =============================================================================

PUBLIC_PATHS = {"/", "/health", "/health/ready", "/health/live"}

@app.middleware("http")
async def auth_middleware(request: Request, call_next):
    """Validate API-key authentication for protected endpoints."""
    if request.url.path in PUBLIC_PATHS:
        return await call_next(request)

    if not config.API_KEYS:
        logger.error("No API_KEYS configured; rejecting protected request")
        return JSONResponse(
            status_code=503,
            content={"error": "API authentication is not configured"},
        )

    api_key = request.headers.get("X-API-Key")
    if not api_key or api_key not in config.API_KEYS:
        return JSONResponse(
            status_code=401,
            content={"error": "Invalid API key"},
        )

    return await call_next(request)

@app.middleware("http")
async def rate_limit_middleware(request: Request, call_next):
    """Apply distributed rate limiting."""
    if request.url.path in ["/health", "/metrics", "/", "/health/ready", "/health/live"]:
        return await call_next(request)
    
    client_ip = request.client.host if request.client else "unknown"
    if not await app.state.rate_limiter.is_allowed(client_ip):
        return JSONResponse(
            status_code=429,
            content={"error": "Rate limit exceeded"},
        )
    
    return await call_next(request)

@app.middleware("http")
async def concurrency_middleware(request: Request, call_next):
    """Limit concurrent requests."""
    async with app.state.semaphore:
        return await call_next(request)

@app.middleware("http")
async def logging_middleware(request: Request, call_next):
    """Log requests with trace IDs."""
    start = time()
    
    trace_id = None
    try:
        span = trace.get_current_span()
        if span:
            span_context = span.get_span_context()
            if span_context.is_valid:
                trace_id = format(span_context.trace_id, "032x")
    except Exception:
        logger.debug("Could not read trace context", exc_info=True)
    
    response = await call_next(request)
    duration = (time() - start) * 1000
    
    log_data = {
        "method": request.method,
        "path": request.url.path,
        "status": response.status_code,
        "duration_ms": duration,
        "client_ip": request.client.host if request.client else None,
        "user_agent": request.headers.get("User-Agent"),
        "trace_id": trace_id
    }
    
    logger.info(json.dumps(log_data))
    return response

# =============================================================================
# Endpoints
# =============================================================================

@app.get("/")
async def root() -> Dict:
    """Root endpoint with service info."""
    return {"name": "search", "version": "2.0"}

@app.get("/health")
async def health() -> Dict:
    """Basic health check."""
    return {"status": "healthy", "timestamp": datetime.now(timezone.utc).isoformat()}

@app.get("/health/ready")
async def readiness() -> Response:
    """Readiness probe with dependency checks."""
    healths = await app.state.health_checker.check_all()
    all_healthy = all(h["healthy"] for h in healths.values())
    
    return JSONResponse(
        status_code=200 if all_healthy else 503,
        content={
            "status": "ready" if all_healthy else "not_ready",
            "dependencies": healths,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        },
    )

@app.get("/health/live")
async def liveness() -> Dict:
    """Liveness probe."""
    return {"status": "alive", "timestamp": datetime.now(timezone.utc).isoformat()}

@app.get("/metrics")
async def get_metrics() -> Dict:
    """Service metrics."""
    cache = app.state.cache
    total = cache.hits + cache.misses
    cb = app.state.circuit_breaker
    
    return {
        "cache_hit_rate": cache.hits / total if total > 0 else 0,
        "cache_size": len(cache.cache),
        "cache_hits": cache.hits,
        "cache_misses": cache.misses,
        "circuit_breaker_state": cb.state.value,
        "circuit_breaker_error_rate": cb.window.error_rate(),
        "circuit_breaker_requests": len(cb.window.requests),
        "circuit_breaker_errors": len(cb.window.errors),
        "uptime_seconds": time() - app.state.start_time,
        "concurrent_requests": config.MAX_CONCURRENT - app.state.semaphore._value
    }

@app.post("/search")
async def search(req: SearchRequest) -> Dict:
    """Execute search with caching and request deduplication."""
    if not req.q or len(req.q.strip()) < 2:
        raise HTTPException(400, "Query too short")
    
    cache_key = hashlib.md5(
        f"{req.q}:{req.size}:{req.from_}:{req.category}:{req.search_type}".encode()
    ).hexdigest()
    
    # Check cache
    cached = await app.state.cache.get(cache_key)
    if cached:
        cached["cached"] = True
        return cached
    
    # Request deduplication
    async with app.state.inflight_lock:
        if cache_key in app.state.inflight_cache:
            event = app.state.inflight_cache[cache_key]
            await event.wait()
            cached = await app.state.cache.get(cache_key)
            if cached:
                cached["cached"] = True
                return cached
    
    event = asyncio.Event()
    async with app.state.inflight_lock:
        app.state.inflight_cache[cache_key] = event
    
    try:
        result = await search_internal(
            app, req.q, req.size, req.from_, req.category, req.search_type
        )
        
        hits = []
        for hit in result.get("hits", {}).get("hits", []):
            src = hit.get("_source", {})
            hits.append({
                "id": hit["_id"],
                "title": src.get("title", ""),
                "content": src.get("content", "")[:200],
                "score": round(hit["_score"], 3),
                "category": src.get("category", ""),
                "updated_at": src.get("updated_at", "")
            })
        
        response = {
            "results": hits,
            "total": result.get("hits", {}).get("total", {}).get("value", 0),
            "search_type": req.search_type,
            "took_ms": result.get("took", 0),
            "cached": False
        }
        
        if hits:
            await app.state.cache.set(cache_key, response)
        
        return response
    finally:
        event.set()
        async with app.state.inflight_lock:
            if cache_key in app.state.inflight_cache:
                del app.state.inflight_cache[cache_key]

# =============================================================================
# Exception Handlers
# =============================================================================

@app.exception_handler(HTTPException)
async def http_exception_handler(request: Request, exc: HTTPException) -> Response:
    """Handle HTTP exceptions with structured logging."""
    logger.warning(
        f"HTTP {exc.status_code}: {exc.detail}",
        extra={"path": request.url.path, "method": request.method}
    )
    return JSONResponse(
        status_code=exc.status_code,
        content={
            "error": exc.detail,
            "status_code": exc.status_code,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        },
    )

@app.exception_handler(Exception)
async def general_exception_handler(request: Request, exc: Exception) -> Response:
    """Handle unhandled exceptions."""
    logger.error(f"Unhandled exception: {exc}", exc_info=True)
    return JSONResponse(
        status_code=500,
        content={
            "error": "Internal server error",
            "timestamp": datetime.now(timezone.utc).isoformat(),
        },
    )