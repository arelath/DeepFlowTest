# Performance Budgets

These budgets are guardrails for local development and CI. Measure regressions on a stable machine and compare medians across repeated runs rather than treating a single sample as definitive.

| Operation | Budget |
| --- | ---: |
| Payload handshake after injection | 5 seconds |
| Typical command round trip | 250 ms |
| Visual tree snapshot with 1,000 nodes | 1 second |
| Selector wait polling interval | 100 ms or greater |
| Stable screenshot wait | 5 seconds |
| Graceful payload shutdown | 5 seconds |

Streaming producers must use bounded queues and report dropped frames rather than allowing unbounded memory growth. Tree capture should honor node and depth limits. Screenshot streams should use the slowest interval that still satisfies the scenario.

Performance changes should be tested separately from correctness changes when possible. Record the target framework, process architecture, node count, image dimensions, and whether the session is local or remote.

