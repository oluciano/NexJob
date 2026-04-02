Read NEXJOB_AI_CONTEXT_MINIMAL.md, ARCHITECTURE.md and CLAUDE.md before answering.

# NexJob — AI Context

This document provides persistent context for AI assistants (Claude, ChatGPT, etc.)
to understand the NexJob project quickly and accurately.

It must be read before executing any task.

---

# Project Overview

NexJob is a **background job processing framework for .NET** focused on:

- predictable execution
- explicit behavior (no hidden magic)
- reliability under failure
- low-latency dispatch

It is designed as a modern alternative to tools like Hangfire, with stronger
support for time-sensitive workflows and failure handling.

---

# Current Version

**v0.4.0 — Production-ready baseline**

---

# Implemented Features

## Core Execution

- `IJob` (no input)
- `IJob<T>` (typed input)
- Dispatcher-based execution model
- Storage-driven job lifecycle

---

## Dispatch System

- Wake-up channel (bounded, non-blocking)
- Near-zero latency for local enqueue
- Polling fallback for distributed scenarios

---

## Scheduling

- Enqueue (fire-and-forget)
- Delayed execution
- Scheduled execution
- Recurring jobs (cron)
- Continuations

---

## Reliability

- Retry policies (global + attribute)
- Dead-letter mechanism
- `IDeadLetterHandler<TJob>` for fallback logic

---

## Deadline Support

- `deadlineAfter: TimeSpan?`
- `ExpiresAt` stored in job record
- Checked before execution
- Expired jobs are skipped
- `JobStatus.Expired`

---

## Observability

- Logging
- OpenTelemetry integration
- Metrics:
  - `nexjob.jobs.expired`

---

## Storage Providers

Production-ready:

- InMemory
- PostgreSQL
- SQL Server
- MongoDB
- Redis

Planned:

- Oracle (exists but NOT packable yet)

---

## Dashboard

- Web dashboard
- Standalone dashboard package

---

## Templates

- NexJob.Templates (CLI scaffolding support)

---

# Architecture Principles

1. Simplicity first
2. Predictability over magic
3. Storage is the source of truth
4. Dispatcher is stateless
5. Explicit failure handling
6. No hidden behavior

---

# Job Model Guidelines

## Use IJob<T> when:

- triggered by API
- triggered by event/webhook
- needs contextual input
- must be reproducible

## Use IJob when:

- batch jobs
- cleanup routines
- periodic tasks

## Rule

Jobs should receive **minimal input** (identity + intent), not full payloads.

---

# Dispatch Model

Hybrid:

- Wake-up (primary for same process)
- Polling (fallback for distributed)

Behavior:

- local enqueue → immediate execution
- external enqueue → eventual consistency

---

# Job Lifecycle

States:

- Enqueued
- Processing
- Succeeded
- Failed
- Dead-letter
- Expired

---

# Reliability Model

- Retry until max attempts
- Then → dead-letter
- Optional handler via `IDeadLetterHandler<TJob>`
- Handler errors must NEVER crash dispatcher

---

# Deadline Model

- Defined at enqueue
- Evaluated before execution
- If expired → job is skipped
- No execution occurs

---

# Storage Constraints

- Storage is authoritative
- Must support atomic dequeue
- No in-memory truth overrides storage

---

# Concurrency Model

- Worker-based execution
- Queue-based isolation
- Throttling supported via attributes

---

# Performance Model

- Wake-up channel removes polling delay
- Bounded signaling avoids contention
- Dispatcher loop is efficient and predictable

---

# Testing Strategy

## Standard Tests

- Unit tests
- Integration tests

## Reliability Suite (IMPORTANT)

Separate project:

