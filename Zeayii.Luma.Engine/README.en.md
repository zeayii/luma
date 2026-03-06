# Zeayii.Luma.Engine

[简体中文](./README.md) | English

The Engine module owns the crawling runtime loop and is the core execution dependency for private consumers.

## Responsibilities

1. Request scheduling and consumption.
2. Download orchestration and response dispatch.
3. Node parsing orchestration and child-node expansion.
4. Batch item persistence orchestration.
5. Completion convergence and stop decision.

## Runtime Flow

```mermaid
sequenceDiagram
    participant Host as Private Host
    participant Engine as LumaEngine
    participant Spider as LumaNode
    participant Sch as IRequestScheduler
    participant D as IDownloader
    participant Sink as IItemSink

    Host->>Engine: RunAsync(spider)
    Engine->>Spider: StartAsync
    Spider-->>Engine: Requests / Children
    Engine->>Sch: Enqueue
    Engine->>Sch: Dequeue
    Engine->>D: DownloadAsync
    D-->>Engine: LumaResponse
    Engine->>Spider: ParseAsync
    Spider-->>Engine: Items / Requests / Children
    Engine->>Sink: StoreBatchAsync
    Engine-->>Host: CrawlRunResult
```

## Runtime Internal Sequence (GitHub Render Friendly)

```mermaid
sequenceDiagram
    autonumber
    participant Engine as LumaEngine
    participant Sch as IRequestScheduler
    participant D as IDownloader
    participant Node as LumaNode Parser
    participant Sink as IItemSink

    Engine->>Node: StartAsync(root)
    Node-->>Engine: SeedRequests
    Engine->>Sch: Enqueue(SeedRequests)
    Engine->>Sch: TryDequeue()
    Sch-->>Engine: Request
    Engine->>D: DownloadAsync(Request)
    D-->>Engine: Response
    Engine->>Node: ParseAsync(Response)
    Node-->>Engine: Items and NextRequests
    Engine->>Sink: StoreBatchAsync(Items)
    Sink-->>Engine: PersistResults
    Engine->>Sch: Enqueue(NextRequests)
    Engine-->>Engine: CheckConvergenceAndContinue
```

## Key Semantics

1. Node registration and node-map updates must be atomic.
2. Completion waiting must be signal-driven (no fixed-delay polling).
3. Downloader must be streaming and body-size bounded.
4. Timeout, cancellation, and failures must converge through a unified stop path.

## Consumer Guidance

1. Private projects should reference this module directly.
2. Extend provider behavior through `ISpider/LumaNode/IItemSink`.
3. Do not embed provider-specific parsing logic into Engine itself.
