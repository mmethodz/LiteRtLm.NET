# Gemma 4 On-Device Receipt Parsing — Technical Design Document

**Version:** 1.0  
**Date:** 2026-04-03  
**Status:** Research / Draft  
**Author:** AI-assisted (GitHub Copilot)  
**Codename:** `SmartReceipt`

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Current State — ReceiptParserService v6.8.1](#2-current-state--receiptparserservice-v681)
3. [Why Gemma 4](#3-why-gemma-4)
   - 3.1 [Model Family Overview](#31-model-family-overview)
   - 3.2 [Vision & Document Understanding Benchmarks](#32-vision--document-understanding-benchmarks)
   - 3.3 [On-Device Inference Performance](#33-on-device-inference-performance)
   - 3.4 [Licensing](#34-licensing)
4. [Architecture — Dual-Path Extraction](#4-architecture--dual-path-extraction)
   - 4.1 [High-Level Flow](#41-high-level-flow)
   - 4.2 [Strategy Selection Logic](#42-strategy-selection-logic)
   - 4.3 [Gemma Integration Point in Existing Pipeline](#43-gemma-integration-point-in-existing-pipeline)
5. [Model Selection & Deployment](#5-model-selection--deployment)
   - 5.1 [Recommended Model: Gemma 4 E4B (INT4)](#51-recommended-model-gemma-4-e4b-int4)
   - 5.2 [Fallback Model: Gemma 4 E2B (INT4)](#52-fallback-model-gemma-4-e2b-int4)
   - 5.3 [Model Delivery Strategy](#53-model-delivery-strategy)
   - 5.4 [Device Compatibility Requirements](#54-device-compatibility-requirements)
6. [**LiteRT-LM .NET MAUI Port — Standalone Deliverable**](#6-litert-lm-net-maui-port--standalone-deliverable)
   - 6.1 [Port Scope & Objectives](#61-port-scope--objectives)
   - 6.2 [Deliverable Definition](#62-deliverable-definition)
   - 6.3 [Native Interop Architecture](#63-native-interop-architecture)
   - 6.4 [Android Implementation (Kotlin Interop)](#64-android-implementation-kotlin-interop)
   - 6.5 [iOS Implementation (Swift/ObjC Interop)](#65-ios-implementation-swiftobjc-interop)
   - 6.6 [Cross-Platform Service Interface](#66-cross-platform-service-interface)
   - 6.7 [Model Lifecycle Management](#67-model-lifecycle-management)
   - 6.8 [Port Validation Test Suite](#68-port-validation-test-suite)
   - 6.9 [DI Registration](#69-di-registration)
   - 6.10 [Port Acceptance Criteria & Gate](#610-port-acceptance-criteria--gate)
   - 6.11 [Port Timeline](#611-port-timeline)
7. [Receipt Parsing — Gemma Integration Layer](#7-receipt-parsing--gemma-integration-layer)
   - 7.1 [Receipt Service Interface](#71-receipt-service-interface)
   - 7.2 [System Prompt](#72-system-prompt)
   - 7.3 [Structured Output Schema](#73-structured-output-schema)
   - 7.4 [Finnish Receipt Specialization](#74-finnish-receipt-specialization)
   - 7.5 [Multi-language Support](#75-multi-language-support)
   - 7.6 [Receipt DI Registration](#76-receipt-di-registration)
8. [Data Model Changes](#8-data-model-changes)
   - 8.1 [OcrExtraction Updates](#81-ocrextraction-updates)
   - 8.2 [ExtractedLineItem Model](#82-extractedlineitem-model)
9. [Confidence Scoring & Validation](#9-confidence-scoring--validation)
   - 9.1 [VAT Math Verification](#91-vat-math-verification)
   - 9.2 [Cross-Validation with Regex Parser](#92-cross-validation-with-regex-parser)
   - 9.3 [Confidence Thresholds](#93-confidence-thresholds)
10. [Performance Budget](#10-performance-budget)
11. [Testing Strategy](#11-testing-strategy)
12. [Migration Path from v6.8.1](#12-migration-path-from-v681)
13. [Risk Analysis](#13-risk-analysis)
14. [Implementation Phases](#14-implementation-phases)
15. [Cost Analysis](#15-cost-analysis)
16. [Future Extensions](#16-future-extensions)

---

## 1. Executive Summary

SiteLedger's FieldLog mobile app currently uses a **three-stage receipt parsing pipeline**:

1. **ReceiptCaptureService** (792 lines) — camera capture, SkiaSharp image optimization
2. **OcrService** — on-device OCR via Plugin.Maui.OCR (ML Kit/Vision) with cloud fallback
3. **ReceiptParserService** (7,094 lines) — regex-based Finnish/EU receipt parser v6.8.1

This pipeline works well for standard Finnish receipts but has inherent limitations:
- **Line items**: The regex parser extracts **totals** reliably but struggles with individual line items — it relies on heuristic column detection that fails on varied receipt layouts
- **OCR errors cascade**: The parser receives raw OCR text and must compensate for character misreads (e.g., `1` vs `l`, comma vs period) — 7,094 lines of defensive code
- **New formats require code changes**: Every new receipt layout requires new regex patterns

**Gemma 4** — Google's latest open model family (released April 2026) — offers a fundamentally different approach: give the model the **receipt image directly** and let it extract structured data using visual understanding, bypassing the brittle OCR→regex chain entirely.

### Key Proposition

| Aspect | Current (v6.8.1) | Proposed (Gemma 4) |
|---|---|---|
| Input | OCR text (lossy) | Receipt image (lossless) |
| Line items | Heuristic, ~40% accuracy | Vision model, ~75-85% expected |
| Totals | ~85% accuracy | ~90-95% expected |
| New receipt formats | Code changes required | Zero-shot generalization |
| Offline capability | ✅ Fully offline | ✅ Fully offline (on-device) |
| Cost per receipt | Free (on-device OCR) | Free (on-device model) |
| Processing time | ~1-3 seconds | ~3-8 seconds (E4B), ~2-5 seconds (E2B) |
| Model download | None | ~3.7 GB (one-time, E4B INT4) |

### Two-Deliverable Architecture

This TDD defines **two distinct deliverables** with a hard gate between them:

```
┌─────────────────────────────────────────────────────────────────────┐
│  DELIVERABLE 1: LiteRT-LM .NET MAUI Port (Section 6)              │
│  ───────────────────────────────────────────────────────           │
│  Standalone reusable infrastructure for running ANY LiteRT-LM     │
│  model on-device from .NET MAUI. Model-agnostic, task-agnostic.   │
│                                                                    │
│  Scope: Native bindings, model download/lifecycle, text & image   │
│         inference APIs, Android first, iOS when SDK stabilizes.   │
│                                                                    │
│  Output: NuGet-packageable library — LiteRtLm.NET                │
│  Timeline: 3-4 weeks                                              │
│  MUST complete before Deliverable 2 begins.                       │
├─────────────────────────────────────────────────────────────────────┤
│                          ▼  GATE  ▼                                │
│  Acceptance: Model loads, text inference works, image inference    │
│  works, memory safety proven, clean API surface validated.         │
├─────────────────────────────────────────────────────────────────────┤
│  DELIVERABLE 2: Gemma 4 Receipt Parsing (Sections 7-16)           │
│  ───────────────────────────────────────────────────────           │
│  Domain-specific receipt extraction built ON TOP of the port.     │
│  Prompt engineering, VAT validation, hybrid mode, UI integration. │
│                                                                    │
│  Depends on: Deliverable 1 passing acceptance gate.               │
│  Timeline: 4-6 weeks                                              │
└─────────────────────────────────────────────────────────────────────┘
```

**Rationale for separation**: The LiteRT-LM port is the highest-risk item in this project (no official .NET bindings exist). By completing it first as a standalone deliverable, we:
1. **De-risk early** — discover interop blockers before investing in domain logic
2. **Create reusable infrastructure** — the port serves future on-device AI features (voice, categorization, translation)
3. **Enable clear go/no-go decision** — if the port fails acceptance criteria, we pivot to cloud-based extraction without wasted receipt-specific work

### Design Constraint: Offline-First

Like all FieldLog features, receipt parsing must work **100% offline**. Gemma 4's on-device inference via LiteRT-LM satisfies this constraint — no cloud API calls required.

---

## 2. Current State — ReceiptParserService v6.8.1

### Pipeline Architecture

```
Camera → ReceiptCaptureService → OptimizeForOcrAsync()
  → SkiaSharp: resize, grayscale, contrast, rotation correction
  → SHA-256 hash for dedup

→ OcrService.ExtractWithFallbackAsync()
  → Plugin.Maui.OCR (ML Kit Android / Vision iOS) — FREE
  → If confidence < threshold → Supabase Edge Function `ocr-receipt`
  → Returns: raw text string

→ ReceiptParserService.ParseAsync()
  → 7,094 lines of regex-based extraction
  → Candidate-scoring architecture (multiple scenarios → best score wins)
  → Multi-language keyword detection (Finnish primary)
  → VatZoneMath engine for Finnish VAT rates (25.5%, 24%, 14%, 13.5%, 10%)
  → Handles OCR corruption patterns
  → Returns: OcrExtraction with per-field confidence
```

### Known Limitations

| Limitation | Impact | Severity |
|---|---|---|
| **Line items unreliable** | Users must manually verify/enter individual items | High |
| **Layout-dependent** | Different receipt formats need new regex patterns | High |
| **OCR error amplification** | 7,094 lines of defensive parsing exist because OCR text is lossy | Medium |
| **No visual context** | Parser cannot see column alignment, font size, separators | Medium |
| **Maintenance burden** | Each new Finnish store chain may need custom parsing logic | Medium |

### What Works Well (Keep)

- **ReceiptCaptureService**: Excellent SkiaSharp image preprocessing — useful for Gemma too
- **SHA-256 dedup**: Prevents duplicate receipt processing
- **OcrExtraction model**: Well-structured output format with confidence scores
- **VatZoneMath**: Finnish VAT verification logic — reusable as post-validation
- **Cloud OCR fallback**: Safety net for low-confidence results

---

## 3. Why Gemma 4

### 3.1 Model Family Overview

Gemma 4 (April 2026) is Google's latest open model family, spanning three architectures:

| Model | Parameters | Effective | Architecture | Context | Modalities |
|---|---|---|---|---|---|
| **Gemma 4 E2B** | ~9.6B total | 2B effective | PLE + Selective Activation | 128K | Text, Image, Audio |
| **Gemma 4 E4B** | ~15B total | 4B effective | PLE + Selective Activation | 128K | Text, Image, Audio |
| Gemma 4 31B | 31B | 31B (dense) | Dense Transformer | 256K | Text, Image |
| Gemma 4 26B A4B | 26B | 4B active/token | Mixture of Experts | 256K | Text, Image |

The **E2B and E4B** models are purpose-built for mobile/edge deployment:
- **Per-Layer Embeddings (PLE)**: Each decoder layer has its own small embedding table — large on disk but fast for lookups
- **Selective parameter activation**: Only 2B or 4B parameters activate per token, despite larger total weights
- **Native multimodal**: Handle text + image + audio input natively (not bolted-on)

### 3.2 Vision & Document Understanding Benchmarks

Gemma 3 (predecessor) vision benchmarks demonstrate strong document understanding:

| Benchmark | Gemma 3 4B | Gemma 3 12B | Gemma 3 27B | Relevance to Receipts |
|---|---|---|---|---|
| **DocVQA** | 72.8 | 82.3 | 85.6 | **Direct** — document Q&A |
| **InfoVQA** | 44.1 | 54.8 | 59.4 | Info extraction from visuals |
| **TextVQA** | 58.9 | 66.5 | 68.6 | Reading text in images |
| **ChartQA** | 63.6 | 74.7 | 76.3 | Structured data from charts |
| **AI2D** | 63.2 | 75.2 | 79.0 | Diagram understanding |

Gemma 4 E4B is expected to **exceed Gemma 3 4B** on these benchmarks due to architectural improvements (PLE, better training data).

**DocVQA 72.8+ is particularly promising** — this benchmark measures the ability to answer questions about document images, which directly maps to receipt parsing (extracting total, vendor, date, line items from a receipt photo).

### 3.3 On-Device Inference Performance

> **⚠️ PROVISIONAL**: All numbers below are from Google's published LiteRT-LM benchmarks (April 2026) for **text-only workloads on reference hardware**. They do **not** account for our specific receipt workload — multimodal image input, prompt shape, output token count, or the overhead of our Kotlin bridge and .NET interop layer. **Treat every timing number in this TDD as an assumption until validated by our own benchmark harness (see §6.10 Proof Task 3).**

LiteRT-LM benchmarks (April 2026, Google-published, text-only):

| Model | Device | Backend | TTFT (ms) | Decode (tok/s) | Model Size |
|---|---|---|---|---|---|
| Gemma 4 E2B | Samsung S26 Ultra | CPU | 557 | 47 | 2.58 GB |
| Gemma 4 E2B | Samsung S26 Ultra | GPU | 3808 | 52 | 2.58 GB |
| Gemma 4 E2B | iPhone 17 Pro | CPU | 532 | 25 | 2.58 GB |
| Gemma 4 E2B | iPhone 17 Pro | GPU | 2878 | 56 | 2.58 GB |
| Gemma 4 E4B | Samsung S26 Ultra | CPU | 195 | 18 | 3.65 GB |
| Gemma 4 E4B | Samsung S26 Ultra | GPU | 1293 | 22 | 3.65 GB |
| Gemma 4 E4B | iPhone 17 Pro | CPU | 159 | 10 | 3.65 GB |
| Gemma 4 E4B | iPhone 17 Pro | GPU | 1189 | 25 | 3.65 GB |

**Key insight:** CPU backend shows **lower TTFT** (time-to-first-token) despite lower throughput. For receipt parsing, TTFT matters more than sustained throughput because the output is short (~200-500 tokens of JSON). **CPU is the likely backend choice** — but this must be validated with image input, which may behave differently.

Back-of-envelope receipt parsing latency estimates (CPU, ~300 output tokens, **ASSUMES text-only perf applies to multimodal — may not**):
- **E2B**: TTFT 550ms + 300/47 ≈ 550 + 6,400 = **~7 seconds** (Samsung S26), **~12.5 seconds** (iPhone 17)
- **E4B**: TTFT 200ms + 300/18 ≈ 200 + 16,700 = **~17 seconds** (Samsung S26)

> **Conclusion**: E2B on CPU is the *likely* sweet spot for receipt parsing — ~7s on flagship Android if text-only benchmarks hold for multimodal. Actual latency for image→JSON roundtrip will be measured in Deliverable 1 (§6.10 Proof Task 3) before committing to receipt-domain work.

### 3.4 Licensing

- **Gemma 4 License**: Apache 2.0 (as of April 2026, per Google blog and model card)
- Commercial use: ✅ Permitted
- Redistribution: ✅ Permitted (can bundle model with app or download on first use)
- No API costs, no usage limits, no telemetry requirements

> **⚠️ PRE-SHIP VERIFICATION REQUIRED**: The Apache 2.0 statement above is based on current public information. Before shipping, the following must be formally verified and documented:
>
> | Check | Source | Status |
> |---|---|---|
> | Exact license text on the specific `.litertlm` bundle we ship/mirror | HuggingFace model card or Google AI page | ⬜ Not yet verified |
> | Redistribution terms for the LiteRT-LM runtime (Maven artifact) | Maven POM / Google AI Edge license | ⬜ Not yet verified |
> | Any additional terms beyond Apache 2.0 (e.g., Gemma Terms of Use) | Google Gemma model page | ⬜ Not yet verified |
> | Hosting source decision: HuggingFace direct vs. self-hosted CDN mirror | Internal decision | ⬜ Not yet decided |
> | NOTICE file / attribution requirements | Apache 2.0 §4(d) | ⬜ Not yet prepared |
>
> This is likely low risk — Apache 2.0 is well understood and Google has publicly stated commercial use is permitted. But "likely low risk" is not the same as "verified". Lock this before production release.

---

## 4. Architecture — Dual-Path Extraction

### 4.1 High-Level Flow

```
                    ┌───────────────────────────┐
                    │   ReceiptCaptureService    │
                    │   Camera → SkiaSharp opt.  │
                    └─────────┬─────────────────┘
                              │ receipt image (byte[])
                              ▼
              ┌───────────────────────────────────┐
              │   IGemmaReceiptService             │
              │   Strategy Selection (§4.2)        │
              └───────┬───────────────┬───────────┘
                      │               │
            ┌─────────▼──────┐  ┌─────▼──────────────┐
            │  Path A: Gemma │  │  Path B: Legacy     │
            │  (on-device    │  │  (OCR → Regex)      │
            │   VLM)         │  │                     │
            │                │  │  OcrService          │
            │  Image → JSON  │  │  → ReceiptParser     │
            │  in ~7 seconds │  │  → OcrExtraction     │
            └─────────┬──────┘  └─────┬──────────────┘
                      │               │
                      ▼               ▼
              ┌───────────────────────────────────┐
              │   Confidence Evaluator             │
              │   VAT Math Check (§9.1)            │
              │   Cross-Validation (§9.2)          │
              └───────────────┬───────────────────┘
                              │
                              ▼
              ┌───────────────────────────────────┐
              │   OcrExtraction (unified output)   │
              │   → Receipt model → SQLite         │
              └───────────────────────────────────┘
```

### 4.2 Strategy Selection Logic

```csharp
public enum ExtractionStrategy
{
    GemmaVision,    // Gemma 4 on-device VLM
    LegacyOcr,      // Plugin.Maui.OCR → ReceiptParserService
    Hybrid           // Both paths → cross-validate
}

// Selection criteria
ExtractionStrategy SelectStrategy(ReceiptImage image)
{
    if (!_gemmaModelAvailable)
        return ExtractionStrategy.LegacyOcr;
    
    if (_deviceMemoryMB < 4096)      // <4GB RAM → skip Gemma
        return ExtractionStrategy.LegacyOcr;
    
    if (_userPreference == "always_gemma")
        return ExtractionStrategy.GemmaVision;
    
    if (_userPreference == "always_legacy")
        return ExtractionStrategy.LegacyOcr;
    
    // Default: hybrid for first 50 receipts (builds confidence data),
    // then auto-select based on historical accuracy
    if (_gemmaReceiptCount < 50)
        return ExtractionStrategy.Hybrid;
    
    return _gemmaAvgConfidence > 0.80
        ? ExtractionStrategy.GemmaVision
        : ExtractionStrategy.Hybrid;
}
```

### 4.3 Gemma Integration Point in Existing Pipeline

The key decision: **Gemma replaces steps 2+3** (OcrService + ReceiptParserService) — it receives the preprocessed image directly and outputs structured JSON, bypassing OCR text entirely.

```
BEFORE:  Image → OCR text → Regex parse → OcrExtraction
AFTER:   Image → Gemma 4 VLM → JSON → OcrExtraction
```

The **ReceiptCaptureService** (step 1) remains unchanged — its SkiaSharp preprocessing (resize, contrast enhancement) benefits both paths.

---

## 5. Model Selection & Deployment

### 5.1 Recommended Model: Gemma 4 E4B (INT4)

| Property | Value |
|---|---|
| Model | `gemma-4-E4B-it-int4` |
| Size on disk | ~3.65 GB |
| Effective parameters | 4B |
| Quantization | INT4 weights, float activations |
| Context window | 128K tokens |
| Modalities | Text + Image + Audio |
| TTFT (Samsung S26, CPU) | ~195 ms |
| Decode speed (CPU) | ~18 tok/s |
| Memory requirement | ~5 GB (INT4) |
| Format | `.litertlm` (LiteRT-LM bundle) |

**Rationale**: Higher accuracy than E2B, especially for structured data extraction. The 5 GB memory footprint is manageable on devices with 8+ GB RAM (most 2024+ flagships).

### 5.2 Fallback Model: Gemma 4 E2B (INT4)

| Property | Value |
|---|---|
| Model | `gemma-4-E2B-it-int4` |
| Size on disk | ~2.58 GB |
| Effective parameters | 2B |
| Memory requirement | ~3.2 GB (INT4) |
| TTFT (Samsung S26, CPU) | ~557 ms |
| Decode speed (CPU) | ~47 tok/s |

**Rationale**: For devices with 4-6 GB RAM. Faster decode speed partially compensates for lower accuracy. Better for mid-range Android phones.

### 5.3 Model Delivery Strategy

```
┌─────────────────────────────────────────────────┐
│  Model Delivery Options                          │
├─────────────────────────────────────────────────┤
│                                                  │
│  Option A: On-Demand Download (RECOMMENDED)      │
│  ─────────────────────────────────────────       │
│  • App ships without model (~100 MB APK)         │
│  • Settings → "Enable Smart Receipt Parsing"     │
│  • Downloads model to app storage on Wi-Fi       │
│  • Progress bar in Settings page                 │
│  • Model persisted until manually cleared        │
│                                                  │
│  Option B: Bundled with App                      │
│  ─────────────────────────────────────────       │
│  • APK/IPA includes model (4+ GB total)          │
│  • ❌ Rejected: violates Play Store 200MB limit  │
│  • ❌ Rejected: massive install size             │
│                                                  │
│  Option C: Android App Bundle + Play Asset       │
│  ─────────────────────────────────────────       │
│  • Play Asset Delivery (on-demand pack)          │
│  • Requires Google Play Services integration     │
│  • iOS equivalent not available                  │
│  • ⚠️ Complex, platform-divergent               │
│                                                  │
└─────────────────────────────────────────────────┘
```

**Decision: Option A — On-Demand Download**

```csharp
// Model storage path
string modelDir = Path.Combine(FileSystem.AppDataDirectory, "models", "gemma4");
string modelPath = Path.Combine(modelDir, "gemma-4-E2B-it-int4.litertlm");
// or "gemma-4-E4B-it-int4.litertlm" for high-end devices

// Download URL (Hugging Face or self-hosted CDN)
const string ModelUrlE2B = "https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm/resolve/main/gemma-4-E2B-it-int4.litertlm";
const string ModelUrlE4B = "https://huggingface.co/litert-community/gemma-4-E4B-it-litert-lm/resolve/main/gemma-4-E4B-it-int4.litertlm";
```

### 5.4 Device Compatibility Requirements

| Tier | RAM | Model | Expected Performance |
|---|---|---|---|
| **High-end** (2024+ flagship) | 8+ GB | E4B INT4 | ~17s per receipt, best accuracy |
| **Mid-range** (2023+ mid) | 6-8 GB | E2B INT4 | ~7s per receipt, good accuracy |
| **Low-end / older** | <6 GB | Legacy only | ReceiptParserService v6.8.1 |

Device capability detection:

```csharp
public static DeviceTier DetectDeviceTier()
{
    var totalRam = GetDeviceTotalRamMB();  // platform-specific
    
    if (totalRam >= 8192)
        return DeviceTier.HighEnd;    // → E4B
    if (totalRam >= 6144)
        return DeviceTier.MidRange;   // → E2B
    return DeviceTier.LowEnd;         // → Legacy OCR only
}
```

---

## 6. LiteRT-LM .NET MAUI Port — Standalone Deliverable

> **⚠️ BLOCKING PREREQUISITE**: This section defines a self-contained deliverable that **must be completed and pass its acceptance gate (§6.10) before any work in Sections 7-16 begins**. The port is model-agnostic and task-agnostic — it enables running *any* LiteRT-LM model from .NET MAUI, not just Gemma for receipt parsing.

### 6.1 Port Scope & Objectives

**What this is**: A reusable .NET MAUI library (`LiteRtLm.NET`) that wraps the Google AI Edge LiteRT-LM native SDKs, providing C# APIs for on-device LLM inference with text and image inputs. Designed for open-source release as the first C# binding library for LiteRT-LM.

**What this is NOT**: Receipt parsing, prompt engineering, VAT validation, or any domain-specific logic. Those are Deliverable 2 (§7+).

| Objective | Description |
|---|---|
| **O1: Native Binding** | Bind the `com.google.ai.edge.litertlm:litertlm-android` Maven artifact for .NET Android |
| **O2: Inference API** | Expose the `Engine` → `Conversation` → `SendMessage` pattern as clean C# async methods (text, image, audio, streaming) |
| **O3: Model Lifecycle** | Download, store, load, unload, delete `.litertlm` model files |
| **O4: Memory Safety** | RAM detection, graceful failure on low-memory devices, `Engine.close()` / forced unload |
| **O5: Backend Selection** | Expose `Backend.GPU()`, `Backend.CPU()`, `Backend.NPU()` hardware acceleration options |
| **O6: Platform Parity** | Android first (stable). iOS stub (`NullLiteRtLmEngine`) until C++/Swift SDK binding stabilizes |
| **O7: Testability** | Standalone validation suite — no consuming-app business logic dependency |
| **O8: Open Source** | MIT-licensed, published to NuGet.org, zero proprietary dependencies |

### 6.2 Deliverable Definition

```
LiteRtLm.NET/
├── LiteRtLm.NET.csproj                  ← Shared C# interfaces & models (netstandard2.0)
│   ├── ILiteRtLmEngine.cs              ← Core inference interface
│   ├── IModelManager.cs                 ← Download/store/delete lifecycle
│   ├── EngineOptions.cs                  ← Engine configuration record
│   ├── ConversationOptions.cs            ← Conversation + sampler config
│   ├── LiteRtLmContent.cs               ← Text/Image/Audio content types
│   ├── InferenceResult.cs               ← Response model
│   ├── BackendType.cs                   ← GPU/CPU/NPU enum
│   ├── DeviceCapability.cs              ← RAM detection, tier classification
│   └── NullLiteRtLmEngine.cs            ← No-op for unsupported platforms
│
├── LiteRtLm.NET.Android/
│   ├── LiteRtLm.NET.Android.csproj          ← Android Binding (Maven reference)
│   ├── Kotlin/LiteRtLmBridge.kt             ← Thin Kotlin shim (coroutine → callback)
│   ├── AndroidLiteRtLmEngine.cs             ← C# → Kotlin interop
│   └── AndroidModelManager.cs                ← File I/O + HttpClient download
│
└── LiteRtLm.NET.iOS/                       (Phase 2 — when Swift SDK stable)
    ├── LiteRtLm.NET.iOS.csproj
    ├── Swift/LiteRtLmBridge.swift
    ├── iOSLiteRtLmEngine.cs
    └── iOSModelManager.cs
```

**Key design principle**: The port library has **zero proprietary dependencies**. It knows nothing about receipts, expenses, or any consuming application’s business models. It is a pure infrastructure layer, published as an open-source NuGet package (`LiteRtLm.NET`) under the MIT license.

**Open-source distribution plan:**
- **Repository**: `github.com/AiMk/LiteRtLm.NET` (MIT license)
- **NuGet packages**: `LiteRtLm.NET` (shared interfaces), `LiteRtLm.NET.Android` (Android binding), `LiteRtLm.NET.iOS` (future)
- **README**: Quick-start guide, API reference, sample project
- **CI**: GitHub Actions — build, test on Android emulator, publish to NuGet.org
- **Versioning**: SemVer. Initial release: `0.1.0-alpha` (aligned with LiteRT-LM SDK v0.10.x which is itself pre-1.0). Will move to `1.0.0` when the LiteRT-LM SDK reaches 1.0 stable.

### 6.3 Native Interop Architecture

LiteRT-LM v0.10.x provides **Kotlin** (Android, stable) and **C++** (cross-platform, stable) APIs, with **Swift** iOS support in development. The SDK is distributed via **Maven** (`com.google.ai.edge.litertlm:litertlm-android`), not as a standalone AAR.

The SDK uses an **`Engine` → `Conversation` → `SendMessage`** pattern (similar to the Gemini API). Key types:

| SDK Type | Purpose |
|---|---|
| `Engine` | Heavyweight object holding model weights. Created from `EngineConfig`. |
| `EngineConfig` | Model path, backend (GPU/CPU/NPU), vision/audio backends, cache dir |
| `Conversation` | Lightweight stateful chat session. Created from engine. |
| `ConversationConfig` | System instruction, initial messages, `SamplerConfig`, tools |
| `SamplerConfig` | `topK`, `topP`, `temperature` |
| `Content` | `Content.Text`, `Content.ImageBytes`, `Content.ImageFile`, `Content.AudioBytes`, `Content.AudioFile` |
| `Message` | `Message.user(contents)`, `Message.model(text)`, `Message.tool(contents)` |
| `Backend` | `Backend.GPU()`, `Backend.CPU()`, `Backend.NPU(nativeLibraryDir)` |

```
┌───────────────────────────────────────────────────────────────────┐
│  .NET MAUI / .NET Android (Shared C#)                            │
│                                                                   │
│  ILiteRtLmEngine                  IModelManager                   │
│  ├── InitializeAsync()            ├── DownloadModelAsync()        │
│  ├── SendMessageAsync()           ├── DeleteModelAsync()          │
│  ├── SendMessageStreamingAsync()  ├── IsModelDownloaded           │
│  ├── CreateConversation()         ├── ModelSizeOnDisk             │
│  ├── IsInitialized                └── GetModelPath()              │
│  └── Dispose()                                                    │
│         ▲                                                         │
│         │ implements (platform-specific)                           │
├─────────┼─────────────────────────────────────────────────────────┤
│ Android │                        iOS (future)                     │
│         │                                                         │
│  AndroidLiteRtLmEngine           iOSLiteRtLmEngine                │
│  (C# → Kotlin Interop)          (C# → ObjC Interop)              │
│         │                                │                        │
│         ▼                                ▼                        │
│  LiteRtLmBridge.kt              LiteRtLmBridge.swift              │
│  (thin Kotlin shim —            (thin Swift shim —                │
│   converts coroutines            converts async/await             │
│   to callbacks)                  to callbacks)                    │
│         │                                │                        │
│         ▼                                ▼                        │
│  litertlm-android (Maven)       LiteRtLm C++ (via xcframework)   │
│  com.google.ai.edge.litertlm    (Google AI Edge SDK)             │
└───────────────────────────────────────────────────────────────────┘
```

### 6.4 Android Implementation (Kotlin Interop)

> **SDK distribution note**: LiteRT-LM v0.10.x is distributed via **Google Maven** (`com.google.ai.edge.litertlm:litertlm-android`), not as a standalone AAR. The .NET Android binding references this Maven artifact directly.

**Step 1: Create Android Binding Library**

```xml
<!-- LiteRtLm.NET.Android.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-android</TargetFramework>
    <SupportedOSPlatformVersion>26</SupportedOSPlatformVersion>
    <RootNamespace>LiteRtLm.NET.Android</RootNamespace>
    <PackageId>LiteRtLm.NET.Android</PackageId>
    <Version>0.1.0-alpha</Version>
    <Authors>AiMk</Authors>
    <Copyright>Copyright (c) 2026 AiMk</Copyright>
    <Description>Android binding for Google AI Edge LiteRT-LM — on-device LLM inference for .NET MAUI and .NET Android. Wraps the Engine/Conversation API with idiomatic C# async patterns.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/AiMk/LiteRtLm.NET</PackageProjectUrl>
    <RepositoryUrl>https://github.com/AiMk/LiteRtLm.NET</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>litert-lm litert llm on-device inference gemma maui android ai google edge</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <!-- Reference the LiteRT-LM Android SDK from Google Maven.
         .NET 9+ supports <AndroidMavenLibrary> for automatic AAR resolution.
         Fallback: download AAR manually and use <AndroidLibrary>. -->
    <AndroidMavenLibrary Include="com.google.ai.edge.litertlm:litertlm-android" Version="0.10.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\LiteRtLm.NET\LiteRtLm.NET.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="..\README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

> **Note**: `<AndroidMavenLibrary>` is supported in .NET 9+. It automatically resolves the AAR from Google Maven and generates C# bindings. If metadata fixups are needed (class renames, access modifiers), use `Metadata.xml` transforms — budget Week 1 for this.

**Step 2: Kotlin Bridge (coroutine → callback bridge)**

The LiteRT-LM Kotlin API uses `suspend` functions and Kotlin `Flow` for streaming. Since .NET Android binding cannot directly consume Kotlin coroutines, we write a thin Kotlin bridge that exposes a callback-based API.

```kotlin
// Kotlin/LiteRtLmBridge.kt
package com.litertlm.dotnet

import com.google.ai.edge.litertlm.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.catch
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Thin bridge between the LiteRT-LM Kotlin SDK and .NET Android binding.
 * Converts coroutine-based async to callback-based for C# interop.
 */
class LiteRtLmBridge {
    private var engine: Engine? = null
    private var conversation: Conversation? = null
    private val scope = CoroutineScope(Dispatchers.Default + SupervisorJob())
    private val initialized = AtomicBoolean(false)
    private var initTimeMs: Long = 0

    /** Initialize the engine with model path and backend config. */
    fun initialize(
        modelPath: String,
        backendType: String,  // "cpu", "gpu", "npu"
        nativeLibraryDir: String?,
        cacheDir: String?,
        callback: InitCallback
    ) {
        scope.launch {
            try {
                val start = System.currentTimeMillis()
                val backend = when (backendType.lowercase()) {
                    "gpu" -> Backend.GPU()
                    "npu" -> Backend.NPU(
                        nativeLibraryDir = nativeLibraryDir ?: ""
                    )
                    else -> Backend.CPU()
                }
                val config = EngineConfig(
                    modelPath = modelPath,
                    backend = backend,
                    visionBackend = Backend.GPU(), // GPU for multimodal
                    cacheDir = cacheDir
                )
                engine = Engine(config)
                engine!!.initialize()
                initTimeMs = System.currentTimeMillis() - start
                initialized.set(true)
                callback.onSuccess(initTimeMs)
            } catch (e: Exception) {
                initialized.set(false)
                callback.onError(e.message ?: "Unknown error")
            }
        }
    }

    /** Create or reset a conversation with sampling params. */
    fun createConversation(
        systemInstruction: String?,
        topK: Int,
        topP: Float,
        temperature: Float
    ) {
        val eng = engine ?: throw IllegalStateException("Engine not initialized")
        conversation?.close()
        val config = ConversationConfig(
            systemInstruction = systemInstruction?.let { Contents.of(it) },
            samplerConfig = SamplerConfig(
                topK = topK,
                topP = topP.toDouble(),
                temperature = temperature.toDouble()
            )
        )
        conversation = eng.createConversation(config)
    }

    /** Send text-only message (blocking). */
    fun sendMessage(text: String, callback: MessageCallback) {
        scope.launch {
            try {
                val conv = conversation
                    ?: throw IllegalStateException("No active conversation")
                val sw = System.currentTimeMillis()
                val message = conv.sendMessage(text)
                val elapsed = System.currentTimeMillis() - sw
                callback.onResult(message.toString(), elapsed)
            } catch (e: Exception) {
                callback.onError(e.message ?: "Unknown error")
            }
        }
    }

    /** Send multimodal message (image bytes + text). */
    fun sendImageMessage(
        text: String,
        imageBytes: ByteArray,
        callback: MessageCallback
    ) {
        scope.launch {
            try {
                val conv = conversation
                    ?: throw IllegalStateException("No active conversation")
                val sw = System.currentTimeMillis()
                val message = conv.sendMessage(Contents.of(
                    Content.ImageBytes(imageBytes),
                    Content.Text(text)
                ))
                val elapsed = System.currentTimeMillis() - sw
                callback.onResult(message.toString(), elapsed)
            } catch (e: Exception) {
                callback.onError(e.message ?: "Unknown error")
            }
        }
    }

    /** Send message with streaming response. */
    fun sendMessageStreaming(text: String, callback: StreamingCallback) {
        scope.launch {
            try {
                val conv = conversation
                    ?: throw IllegalStateException("No active conversation")
                conv.sendMessageAsync(text)
                    .catch { callback.onError(it.message ?: "Stream error") }
                    .collect { message ->
                        callback.onToken(message.toString())
                    }
                callback.onDone()
            } catch (e: Exception) {
                callback.onError(e.message ?: "Unknown error")
            }
        }
    }

    fun isInitialized(): Boolean = initialized.get()
    fun getInitTimeMs(): Long = initTimeMs

    fun dispose() {
        conversation?.close()
        conversation = null
        engine?.close()
        engine = null
        initialized.set(false)
        scope.cancel()
    }

    // Callback interfaces for C# interop
    interface InitCallback {
        fun onSuccess(loadTimeMs: Long)
        fun onError(error: String)
    }

    interface MessageCallback {
        fun onResult(text: String, elapsedMs: Long)
        fun onError(error: String)
    }

    interface StreamingCallback {
        fun onToken(text: String)
        fun onDone()
        fun onError(error: String)
    }
}
```

**Step 3: C# Platform Service**

```csharp
// AndroidLiteRtLmEngine.cs
using Com.Litertlm.Dotnet;

namespace LiteRtLm.NET.Android;

public class AndroidLiteRtLmEngine : ILiteRtLmEngine
{
    private readonly LiteRtLmBridge _bridge = new();
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    public bool IsInitialized => _bridge.IsInitialized();
    public long InitializeTimeMs => _bridge.GetInitTimeMs();

    public Task<bool> InitializeAsync(EngineOptions options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        ct.Register(() => tcs.TrySetCanceled());

        _bridge.Initialize(
            options.ModelPath,
            options.Backend.ToString().ToLowerInvariant(),
            options.NativeLibraryDir,
            options.CacheDir,
            new InitCallbackImpl(tcs));

        return tcs.Task;
    }

    public void CreateConversation(ConversationOptions? options = null)
    {
        var opts = options ?? ConversationOptions.Default;
        _bridge.CreateConversation(
            opts.SystemInstruction,
            opts.TopK, opts.TopP, opts.Temperature);
    }

    public async Task<InferenceResult> SendMessageAsync(
        string text, CancellationToken ct = default)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<InferenceResult>();
            ct.Register(() => tcs.TrySetCanceled());

            _bridge.SendMessage(text, new MessageCallbackImpl(tcs));
            return await tcs.Task;
        }
        finally { _inferenceLock.Release(); }
    }

    public async Task<InferenceResult> SendMessageAsync(
        string text, byte[] imageBytes, CancellationToken ct = default)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<InferenceResult>();
            ct.Register(() => tcs.TrySetCanceled());

            _bridge.SendImageMessage(text, imageBytes, new MessageCallbackImpl(tcs));
            return await tcs.Task;
        }
        finally { _inferenceLock.Release(); }
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
            _bridge.SendMessageStreaming(text, new StreamingCallbackImpl(channel.Writer));

            await foreach (var token in channel.Reader.ReadAllAsync(ct))
                yield return token;
        }
        finally { _inferenceLock.Release(); }
    }

    public void Dispose()
    {
        _inferenceLock.Dispose();
        _bridge.Dispose();
    }

    // --- Callback adapters (Java → C# TaskCompletionSource) ---

    private class InitCallbackImpl(TaskCompletionSource<bool> tcs)
        : Java.Lang.Object, LiteRtLmBridge.IInitCallback
    {
        public void OnSuccess(long loadTimeMs) => tcs.TrySetResult(true);
        public void OnError(string error) => tcs.TrySetResult(false);
    }

    private class MessageCallbackImpl(TaskCompletionSource<InferenceResult> tcs)
        : Java.Lang.Object, LiteRtLmBridge.IMessageCallback
    {
        public void OnResult(string text, long elapsedMs) =>
            tcs.TrySetResult(new InferenceResult
            {
                Success = true, Text = text, ProcessingTimeMs = (int)elapsedMs
            });

        public void OnError(string error) =>
            tcs.TrySetResult(new InferenceResult { Success = false, Error = error });
    }

    private class StreamingCallbackImpl(
        System.Threading.Channels.ChannelWriter<string> writer)
        : Java.Lang.Object, LiteRtLmBridge.IStreamingCallback
    {
        public void OnToken(string text) => writer.TryWrite(text);
        public void OnDone() => writer.Complete();
        public void OnError(string error) => writer.Complete(new Exception(error));
    }
}
```

### 6.5 iOS Implementation (Swift/ObjC Interop)

> **Note**: LiteRT-LM Swift SDK is currently "In Dev" (as of April 2026). The cross-platform C++ API is stable and could be used for iOS via native binding, but this is deferred to Phase 4 (§14). In the interim, iOS receives a `NullLiteRtLmEngine` that always returns `IsInitialized = false`, causing callers to fall back to cloud/legacy paths.

When the Swift/C++ SDK stabilizes for iOS:
1. Thin Swift bridge wrapping the LiteRT-LM C++ or Swift SDK
2. ObjC bridge for .NET MAUI binding
3. C# platform service implementing `ILiteRtLmEngine`

### 6.6 Cross-Platform Service Interface

These interfaces are the **public API surface** of the port. They live in the shared `LiteRtLm.NET` project (targeting `netstandard2.0`) and have **no platform-specific or application-specific dependencies**.

> **Design note**: The interface mirrors the LiteRT-LM SDK's `Engine` → `Conversation` → `SendMessage` architecture: `ILiteRtLmEngine` owns the model lifecycle and creates conversations for stateful chat.

```csharp
// ILiteRtLmEngine.cs — Core inference interface

namespace LiteRtLm.NET;

/// <summary>
/// Platform-agnostic interface for on-device LLM inference via LiteRT-LM.
/// Maps to the SDK's Engine + Conversation pattern.
/// Implementations: AndroidLiteRtLmEngine, iOSLiteRtLmEngine, NullLiteRtLmEngine.
/// </summary>
public interface ILiteRtLmEngine : IDisposable
{
    /// <summary>Whether the engine is initialized and ready for inference.</summary>
    bool IsInitialized { get; }

    /// <summary>Time taken to initialize the engine (ms). 0 if not initialized.</summary>
    long InitializeTimeMs { get; }

    /// <summary>Initialize the engine: loads model weights into memory.
    /// This can take 5-10 seconds — always call on a background thread.</summary>
    Task<bool> InitializeAsync(EngineOptions options, CancellationToken ct = default);

    /// <summary>Create or reset a conversation with optional configuration.
    /// Conversations are lightweight — create new ones for different tasks.</summary>
    void CreateConversation(ConversationOptions? options = null);

    /// <summary>Send a text message and get a response.</summary>
    Task<InferenceResult> SendMessageAsync(string text, CancellationToken ct = default);

    /// <summary>Send a multimodal message (text + image bytes) and get a response.
    /// Requires EngineOptions.VisionBackend to be set.</summary>
    Task<InferenceResult> SendMessageAsync(string text, byte[] imageBytes, CancellationToken ct = default);

    /// <summary>Send a text message and stream response tokens.</summary>
    IAsyncEnumerable<string> SendMessageStreamingAsync(string text, CancellationToken ct = default);
}

// IModelManager.cs — Model download & storage lifecycle

/// <summary>
/// Manages model file download, storage, and deletion.
/// Decoupled from inference — can download without loading.
/// </summary>
public interface IModelManager
{
    /// <summary>Whether the model file exists on disk.</summary>
    bool IsModelDownloaded { get; }

    /// <summary>Model file size on disk in bytes. 0 if not downloaded.</summary>
    long ModelSizeOnDisk { get; }

    /// <summary>Full path to the model file.</summary>
    string GetModelPath();

    /// <summary>Download model from URL to local storage. Reports progress 0.0-1.0.</summary>
    Task<bool> DownloadModelAsync(string url, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Delete model file from local storage.</summary>
    Task DeleteModelAsync();
}
```

```csharp
// EngineOptions.cs — Engine configuration (maps to SDK EngineConfig)

namespace LiteRtLm.NET;

/// <summary>
/// Engine-level configuration. Corresponds to the LiteRT-LM SDK's EngineConfig.
/// Immutable — create a new instance to change settings.
/// </summary>
public record EngineOptions
{
    /// <summary>Absolute path to the .litertlm model file.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Hardware backend for text inference. Default: CPU.</summary>
    public BackendType Backend { get; init; } = BackendType.Cpu;

    /// <summary>Hardware backend for vision (image) inference.
    /// Set to GPU for multimodal use. Null = not configured.</summary>
    public BackendType? VisionBackend { get; init; }

    /// <summary>Hardware backend for audio inference. Null = not configured.</summary>
    public BackendType? AudioBackend { get; init; }

    /// <summary>Optional cache directory for compiled model artifacts.
    /// Significantly speeds up subsequent initializations (2nd+ load).</summary>
    public string? CacheDir { get; init; }

    /// <summary>Native library directory (required for NPU backend on some devices).</summary>
    public string? NativeLibraryDir { get; init; }
}

// ConversationOptions.cs — Per-conversation config (maps to SDK ConversationConfig + SamplerConfig)

/// <summary>
/// Conversation-level configuration. Includes sampling parameters.
/// Each CreateConversation() call can use different options.
/// </summary>
public record ConversationOptions
{
    /// <summary>System instruction prepended to the conversation.</summary>
    public string? SystemInstruction { get; init; }

    /// <summary>Top-K sampling. 1 = greedy/deterministic. Default: 1.</summary>
    public int TopK { get; init; } = 1;

    /// <summary>Top-P (nucleus) sampling. 1.0 = disabled. Default: 1.0f.</summary>
    public float TopP { get; init; } = 1.0f;

    /// <summary>Temperature. 0.0 = deterministic. Default: 0.0f.</summary>
    public float Temperature { get; init; } = 0.0f;

    /// <summary>Default options for deterministic receipt-parsing use (TopK=1, Temp=0).</summary>
    public static ConversationOptions Default { get; } = new();
}

// BackendType.cs — Hardware acceleration enum (maps to SDK Backend)

/// <summary>
/// Hardware acceleration backend for LiteRT-LM inference.
/// Maps to the SDK's Backend.GPU(), Backend.CPU(), Backend.NPU().
/// </summary>
public enum BackendType
{
    /// <summary>CPU inference (universal, slower).</summary>
    Cpu = 0,

    /// <summary>GPU inference (faster, requires GPU driver support).</summary>
    Gpu = 1,

    /// <summary>NPU inference (fastest on supported SoCs, e.g. Qualcomm, MediaTek).
    /// Requires NativeLibraryDir in EngineOptions.</summary>
    Npu = 2
}

// InferenceResult.cs — Response model

public record InferenceResult
{
    public bool Success { get; init; }
    public string Text { get; init; } = "";
    public int ProcessingTimeMs { get; init; }
    public string? Error { get; init; }
}
```

```csharp
// DeviceCapability.cs — RAM detection & tier classification
// NOTE: This is a *shared model* class. Platform-specific RAM detection
// is injected via a Func<long> or provided by the platform project.

namespace LiteRtLm.NET;

public enum DeviceTier
{
    HighEnd,    // 8+ GB RAM → E4B model
    MidRange,   // 6-8 GB RAM → E2B model
    LowEnd      // <6 GB RAM → no on-device LLM
}

public static class DeviceCapability
{
    /// <summary>
    /// Classify device tier based on total RAM.
    /// Platform-specific RAM retrieval is handled by the caller.
    /// </summary>
    public static DeviceTier ClassifyTier(long totalRamMB) => totalRamMB switch
    {
        >= 8192 => DeviceTier.HighEnd,
        >= 6144 => DeviceTier.MidRange,
        _ => DeviceTier.LowEnd
    };

    public static bool CanRunLiteRtLm(long totalRamMB) =>
        ClassifyTier(totalRamMB) != DeviceTier.LowEnd;
}

// NullLiteRtLmEngine.cs — No-op for unsupported platforms

public class NullLiteRtLmEngine : ILiteRtLmEngine
{
    public bool IsInitialized => false;
    public long InitializeTimeMs => 0;

    public Task<bool> InitializeAsync(EngineOptions options, CancellationToken ct = default)
        => Task.FromResult(false);

    public void CreateConversation(ConversationOptions? options = null) { }

    public Task<InferenceResult> SendMessageAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new InferenceResult { Success = false, Error = "LiteRT-LM not available on this platform" });

    public Task<InferenceResult> SendMessageAsync(string text, byte[] imageBytes, CancellationToken ct = default)
        => Task.FromResult(new InferenceResult { Success = false, Error = "LiteRT-LM not available on this platform" });

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public void Dispose() { }
}
```

### 6.7 Model Lifecycle Management

```csharp
// ModelManager.cs — shared implementation (platform-agnostic file I/O)

public class ModelManager : IModelManager
{
    private readonly string _modelDir;
    private readonly string _modelFileName;
    private readonly HttpClient _httpClient;

    public ModelManager(string modelFileName, HttpClient? httpClient = null)
    {
        _modelDir = Path.Combine(FileSystem.AppDataDirectory, "models", "litert-lm");
        _modelFileName = modelFileName;
        _httpClient = httpClient ?? new HttpClient();
    }

    public bool IsModelDownloaded => File.Exists(GetModelPath());

    public long ModelSizeOnDisk => IsModelDownloaded
        ? new FileInfo(GetModelPath()).Length
        : 0;

    public string GetModelPath() => Path.Combine(_modelDir, _modelFileName);

    public async Task<bool> DownloadModelAsync(
        string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_modelDir);
            var tempPath = GetModelPath() + ".tmp";

            using var response = await _httpClient.GetAsync(url,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long bytesRead = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            int read;
            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;
                if (totalBytes > 0)
                    progress?.Report((double)bytesRead / totalBytes);
            }

            // Atomic rename — prevents partial file on crash/cancel
            File.Move(tempPath, GetModelPath(), overwrite: true);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public Task DeleteModelAsync()
    {
        var path = GetModelPath();
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
```

### 6.8 Port Validation Test Suite

The port must pass these tests **before** any receipt-specific work (§7+) begins:

```csharp
// Tests are run on a physical Android device (emulator cannot guarantee RAM/GPU)

[TestFixture]
public class LiteRtLmPortValidation
{
    private AndroidLiteRtLmEngine _engine = null!;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        _engine = new AndroidLiteRtLmEngine();
        await _engine.InitializeAsync(new EngineOptions
        {
            ModelPath = TestConstants.ModelPathE2B,
            Backend = BackendType.Gpu,
            CacheDir = TestConstants.CacheDir
        });
        _engine.CreateConversation(ConversationOptions.Default);
    }

    [OneTimeTearDown]
    public void TearDown() => _engine.Dispose();

    // ── T1: Model Download ──
    [Test]
    public async Task T1_DownloadModel_CompletesSuccessfully()
    {
        var manager = new ModelManager("gemma-4-E2B-it-int4.litertlm");
        var progress = new Progress<double>(p => Console.WriteLine($"Download: {p:P0}"));
        var result = await manager.DownloadModelAsync(TestConstants.ModelUrlE2B, progress);
        Assert.That(result, Is.True);
        Assert.That(manager.IsModelDownloaded, Is.True);
        Assert.That(manager.ModelSizeOnDisk, Is.GreaterThan(2_000_000_000)); // >2GB
    }

    // ── T2: Engine Initialize ──
    [Test]
    public async Task T2_InitializeEngine_CompletesUnder10Seconds()
    {
        using var engine = new AndroidLiteRtLmEngine();
        var sw = Stopwatch.StartNew();
        var ready = await engine.InitializeAsync(new EngineOptions
        {
            ModelPath = TestConstants.ModelPathE2B,
            Backend = BackendType.Gpu,
            CacheDir = TestConstants.CacheDir
        });
        sw.Stop();
        Assert.That(ready, Is.True);
        Assert.That(engine.IsInitialized, Is.True);
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(10_000));
    }

    // ── T3: Text Generation (SendMessage) ──
    [Test]
    public async Task T3_SendMessage_ReturnsNonEmptyResponse()
    {
        var result = await _engine.SendMessageAsync("What is 2 + 2? Answer with just the number.");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Text, Does.Contain("4"));
        Assert.That(result.ProcessingTimeMs, Is.GreaterThan(0));
    }

    // ── T4: Image + Text Generation (Multimodal) ──
    [Test]
    public async Task T4_SendImageMessage_ReturnsDescription()
    {
        var imageBytes = await File.ReadAllBytesAsync(TestConstants.SampleImagePath);
        var result = await _engine.SendMessageAsync(
            "Describe what you see in this image in one sentence.", imageBytes);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Text.Length, Is.GreaterThan(10));
    }

    // ── T5: Dispose/Reinitialize Safety ──
    [Test]
    public async Task T5_DisposeAndReinit_NoMemoryLeak()
    {
        using var engine = new AndroidLiteRtLmEngine();
        await engine.InitializeAsync(new EngineOptions
        {
            ModelPath = TestConstants.ModelPathE2B, Backend = BackendType.Cpu
        });
        Assert.That(engine.IsInitialized, Is.True);
        engine.Dispose();
        // After dispose, calls should fail gracefully
        var result = await engine.SendMessageAsync("test");
        Assert.That(result.Success, Is.False);
    }

    // ── T6: Concurrent Safety ──
    [Test]
    public async Task T6_ConcurrentInference_Serialized()
    {
        _engine.CreateConversation(); // fresh conversation
        var t1 = _engine.SendMessageAsync("Say hello");
        var t2 = _engine.SendMessageAsync("Say goodbye");
        var results = await Task.WhenAll(t1, t2);
        Assert.That(results.All(r => r.Success), Is.True);
    }

    // ── T7: Cancellation ──
    [Test]
    public void T7_Cancellation_DoesNotCrash()
    {
        using var cts = new CancellationTokenSource(100); // 100ms timeout
        Assert.DoesNotThrowAsync(async () =>
        {
            try { await _engine.SendMessageAsync("Write a long essay", cts.Token); }
            catch (OperationCanceledException) { /* expected */ }
        });
    }

    // ── T8: DeviceCapability ──
    [Test]
    public void T8_DeviceCapability_ReturnsValidTier()
    {
        // Platform-specific RAM retrieval (Android)
        var activityManager = (Android.App.ActivityManager)
            Android.App.Application.Context.GetSystemService(Android.Content.Context.ActivityService)!;
        var memInfo = new Android.App.ActivityManager.MemoryInfo();
        activityManager.GetMemoryInfo(memInfo);
        var ramMB = memInfo.TotalMem / (1024 * 1024);

        var tier = DeviceCapability.ClassifyTier(ramMB);
        Assert.That(tier, Is.Not.EqualTo(default(DeviceTier)));
        // On test device with ≥8GB RAM:
        Assert.That(DeviceCapability.CanRunLiteRtLm(ramMB), Is.True);
    }

    // ── T9: Null Engine (platform fallback) ──
    [Test]
    public async Task T9_NullEngine_ReturnsGracefulFailure()
    {
        var nullEngine = new NullLiteRtLmEngine();
        Assert.That(nullEngine.IsInitialized, Is.False);
        var result = await nullEngine.SendMessageAsync("test");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    // ── T10: Streaming Response ──
    [Test]
    public async Task T10_StreamingResponse_YieldsTokens()
    {
        _engine.CreateConversation();
        var tokens = new List<string>();
        await foreach (var token in _engine.SendMessageStreamingAsync("Count from 1 to 5."))
        {
            tokens.Add(token);
        }
        Assert.That(tokens.Count, Is.GreaterThan(1)); // multiple tokens streamed
        var fullText = string.Join("", tokens);
        Assert.That(fullText, Does.Contain("1"));
    }

    // ── T11: Backend Selection ──
    [Test]
    public async Task T11_CpuBackend_WorksAsFallback()
    {
        using var cpuEngine = new AndroidLiteRtLmEngine();
        var ready = await cpuEngine.InitializeAsync(new EngineOptions
        {
            ModelPath = TestConstants.ModelPathE2B,
            Backend = BackendType.Cpu // explicit CPU fallback
        });
        Assert.That(ready, Is.True);
        cpuEngine.CreateConversation();
        var result = await cpuEngine.SendMessageAsync("Say hello");
        Assert.That(result.Success, Is.True);
    }
}
```

### 6.9 DI Registration

```csharp
// MauiProgram.cs — port registration (Deliverable 1)
// These registrations are independent of any receipt/expense logic.

#if ANDROID
builder.Services.AddSingleton<ILiteRtLmEngine, LiteRtLm.NET.Android.AndroidLiteRtLmEngine>();
#elif IOS
// Deferred — iOS C++/Swift SDK binding not yet implemented
builder.Services.AddSingleton<ILiteRtLmEngine, NullLiteRtLmEngine>();
#else
builder.Services.AddSingleton<ILiteRtLmEngine, NullLiteRtLmEngine>();
#endif

builder.Services.AddSingleton<IModelManager>(sp =>
    new ModelManager("gemma-4-E2B-it-int4.litertlm", sp.GetService<HttpClient>()));
```

### 6.10 Port Acceptance Criteria & Gate

> **Philosophy**: Deliverable 1 exists to answer one question: *can we run a LiteRT-LM model from .NET MAUI reliably enough to build on?* Everything else is secondary until that question is answered. The gate is structured around **three core proof tasks** that must pass, followed by **five go/no-go questions** that determine whether Deliverable 2 starts.

#### 6.10.1 Three Core Proof Tasks

These are the only things that matter in Deliverable 1. If any of these three fails, Deliverable 2 does not start.

**Proof Task 1: Text inference works end-to-end on Android**

Can the binding load, the bridge call through, and a text response come back?

| Criterion | Test | Pass Condition |
|---|---|---|
| Maven binding compiles | `dotnet build` against `AndroidMavenLibrary` | No errors |
| Kotlin bridge callable from C# | Callback fires from `LiteRtLmBridge.kt` **through** .NET binding | Non-null response |
| Engine initializes | `InitializeAsync()` completes | `IsInitialized == true` |
| Text inference returns coherent output | `SendMessageAsync("What is 2+2?")` | Response contains "4" |
| Dispose is clean | `Dispose()` followed by `SendMessageAsync()` | No crash, returns `Success = false` |

**Proof Task 2: Image inference works end-to-end on Android**

> **This is the single most important acceptance criterion.** The entire receipt-parsing value proposition depends on feeding a receipt image directly to the model and getting structured output back. If image→structured-output roundtrip does not work through the Kotlin bridge and .NET boundary, there is no Deliverable 2.

| Criterion | Test | Pass Condition |
|---|---|---|
| Image bytes pass through bridge without corruption | Send known test image through `SendImageMessage()` | Non-error response |
| Model describes image content | Send receipt photo + "Describe this image" | Response references receipt-like content |
| Image + structured prompt returns parseable output | Send receipt photo + JSON extraction prompt | Response is valid JSON (even if imperfect) |
| No excessive memory spike from image transfer | Monitor `Debug.GetTotalMemory()` before/after | Peak delta < 500 MB above model baseline |
| Multimodal with `VisionBackend` configured | Set `EngineOptions.VisionBackend = BackendType.Gpu` | Image inference still works (may be faster) |

**Proof Task 3: Repeated init/infer/dispose cycle is memory-safe**

Can the engine survive real-world usage patterns without leaking, crashing, or getting OOM-killed?

| Criterion | Test | Pass Condition |
|---|---|---|
| 10× init/dispose cycle | Loop: `InitializeAsync()` → `SendMessageAsync()` → `Dispose()` × 10 | No crash, no ANR, no OOM kill |
| Memory stable across parses | 10 sequential `SendMessageAsync()` calls, log memory after each | RSS growth < 100 MB across all 10 |
| 3 image parses without memory growth | `SendMessageAsync(text, imageBytes)` × 3 with same image | Peak memory does not ratchet up |
| Concurrent safety | Two parallel `SendMessageAsync()` calls | Both succeed (serialized by `SemaphoreSlim`) |
| Cancellation safety | Cancel mid-inference with `CancellationToken` | No crash, no resource leak |
| App survives in background | Init engine → put app to background 30s → resume → infer | Inference succeeds or fails gracefully |

#### 6.10.2 Five Go/No-Go Gate Questions

After the three proof tasks pass, answer these five questions. All five must be **Yes** to proceed to Deliverable 2.

| # | Question | How to Answer | Fail Action |
|---|---|---|---|
| **G1** | Can Android load a LiteRT-LM model from MAUI through the binding, initialize it reliably, and dispose it safely? | Proof Task 1 + Proof Task 3 pass | ❌ **Abandon** on-device path |
| **G2** | Can the bridge pass image input — not just text — and get valid responses back? | Proof Task 2 passes | ⚠️ **Pivot** to text-only + legacy OCR for images |
| **G3** | Can E2B complete one real receipt extraction within an acceptable latency envelope on the target Android device class? | Run actual receipt photo through JSON extraction prompt on test device. Measure wall-clock time. Target: < 20s. | 🔄 **Investigate** backend selection (GPU/NPU), image preprocessing, shorter prompts. If still >30s: reassess |
| **G4** | Can memory stay stable across repeated parses without app kills or permanent fragmentation? | Proof Task 3 (memory criteria) passes. Additionally: run 10 receipt images sequentially, check for RSS ratchet | 🔄 **Iterate** — likely a bridge leak or missing `close()`. Fix and re-test. Max 1 week |
| **G5** | Can the model produce parseable structured output often enough that the fallback path remains an exception, not the norm? | Run 5 different receipt images with JSON extraction prompt. ≥ 4/5 must return parseable JSON | ⚠️ If <3/5: investigate constrained decoding. If constrained decoding unavailable through bridge: **Pivot** to text extraction + regex postprocessing |

#### 6.10.3 Supporting Acceptance Criteria

These are important but **secondary** — they do not block the gate if the three proof tasks and five go/no-go questions pass.

| # | Criterion | Measurement | Priority |
|---|---|---|---|
| **AC-S1** | Model downloads to device | T1 passes, file on disk >2GB | High (but mechanical) |
| **AC-S2** | RAM detection works | `DeviceCapability.ClassifyTier()` returns correct tier on test device | Medium |
| **AC-S3** | Null fallback works | `NullLiteRtLmEngine.SendMessageAsync()` returns `Success = false` | Medium |
| **AC-S4** | Streaming works | `SendMessageStreamingAsync()` yields multiple tokens | Low (nice-to-have for D1) |
| **AC-S5** | Backend selection works | CPU fallback functional when GPU unavailable | Medium |
| **AC-S6** | No application dependencies | Port project has zero references to consuming app | High |
| **AC-S7** | API surface documented | All public interfaces have XML doc comments | Medium |

#### 6.10.4 Gate Decision Matrix

| Outcome | Action |
|---|---|
| All 3 proof tasks pass + all 5 gate questions = Yes | ✅ **Proceed** to Deliverable 2 (receipt parsing) |
| G1 = No (binding fundamentally broken) | ❌ **Abandon** on-device LLM. Investigate cloud-based VLM via Supabase Edge Function |
| G2 = No (image inference broken) | ⚠️ **Pivot**: Use port for text-only tasks. Keep legacy OCR→regex for receipt images. Reassess when LiteRT-LM multimodal matures |
| G3 = No (too slow) | 🔄 **Iterate**: Try GPU/NPU backend, smaller image, shorter prompt. If still unacceptable after 1 week: pivot to cloud |
| G4 = No (memory unstable) | 🔄 **Iterate**: Debug leak, fix bridge lifecycle. Max 1 additional week |
| G5 = No (JSON unreliable) | ⚠️ **Pivot**: Use plain-text extraction + regex postprocessing instead of direct JSON output. Increases Deliverable 2 complexity but may be viable |

### 6.11 Port Timeline

| Week | Tasks | Output |
|---|---|---|
| **Week 1** | Configure `<AndroidMavenLibrary>` for LiteRT-LM. Create binding project. Resolve Kotlin binding `Metadata.xml` transforms. Build `LiteRtLmBridge.kt` | Compiling Maven binding — answers **G1** |
| **Week 2** | Implement `AndroidLiteRtLmEngine`, `ModelManager`, `DeviceCapability`. Download E2B model. Engine init + **text-only inference** (Core Proof Task #1). Begin **image inference** (Core Proof Task #2) | T1-T3 passing. G1, G2 answered |
| **Week 3** | Complete multimodal inference. Streaming. Backend selection. Concurrency. **Memory measurement protocol** (§13.4, Core Proof Task #3). Structured output testing (G5) | T4-T11 passing. G3, G4, G5 answered |
| **Week 4** | API polish, XML docs, edge case hardening. **Run full acceptance gate** (§6.10) | All 3 core proof tasks pass. All 5 gate questions answered. Supporting criteria AC-S1 through AC-S7 verified |

**Estimated effort**: ~80 person-hours (4 weeks, ~4 hours/day dedicated)

---

## 7. Receipt Parsing — Gemma Integration Layer

> **Prerequisite**: Section 6 (LiteRT-LM Port) must have passed the acceptance gate — all three core proof tasks (§6.10.1) and all five go/no-go gate questions (§6.10.2) must be answered affirmatively before starting this section.

### 7.1 Receipt Service Interface

This layer sits **on top of** the port (§6) and adds receipt-specific logic:

```csharp
// Services/IGemmaReceiptService.cs
// Depends on: ILiteRtLmEngine (from port), IModelManager (from port)
// Depends on: ReceiptParserService (legacy fallback), OcrExtraction (output model)

/// <summary>
/// On-device receipt parsing using Gemma 4 VLM.
/// Falls back to legacy OCR pipeline when model is unavailable.
/// Built on top of the LiteRT-LM .NET MAUI port (§6).
/// </summary>
public interface IGemmaReceiptService
{
    /// <summary>Whether the Gemma model is downloaded and loaded.</summary>
    bool IsModelAvailable { get; }

    /// <summary>Currently loading the model into memory.</summary>
    bool IsModelLoading { get; }

    /// <summary>Approximate model size on disk in bytes.</summary>
    long ModelSizeBytes { get; }

    /// <summary>Download the model file (Wi-Fi recommended). Reports progress 0.0-1.0.</summary>
    Task<bool> DownloadModelAsync(IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Delete the model from local storage.</summary>
    Task DeleteModelAsync();

    /// <summary>Initialize the engine and load model. Call once after download, or at app startup.</summary>
    Task<bool> InitializeModelAsync(CancellationToken ct = default);

    /// <summary>Dispose engine (frees RAM). Re-initialization needed to use again.</summary>
    void DisposeEngine();

    /// <summary>
    /// Extract receipt data from image using Gemma VLM.
    /// Returns null if model unavailable (caller should fall back to legacy).
    /// </summary>
    Task<GemmaReceiptResult?> ExtractReceiptAsync(byte[] imageBytes, string locale = "fi", CancellationToken ct = default);
}

/// <summary>Raw result from Gemma extraction before validation.</summary>
public record GemmaReceiptResult
{
    public string? VendorName { get; init; }
    public DateTime? ReceiptDate { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? SubtotalAmount { get; init; }
    public decimal? VatAmount { get; init; }
    public string? Currency { get; init; }
    public string? PaymentMethod { get; init; }
    public List<GemmaLineItem> LineItems { get; init; } = new();
    public List<GemmaVatBreakdown> VatBreakdown { get; init; } = new();
    public double Confidence { get; init; }
    public int ProcessingTimeMs { get; init; }
    public string RawJsonResponse { get; init; } = "";
}

public record GemmaLineItem
{
    public string Description { get; init; } = "";
    public decimal Quantity { get; init; } = 1;
    public string? Unit { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice { get; init; }
    public decimal? VatRate { get; init; }
}

public record GemmaVatBreakdown
{
    public decimal Rate { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal VatAmount { get; init; }
}
```

The `GemmaReceiptService` implementation delegates to `ILiteRtLmEngine` from the port:

```csharp
// Services/GemmaReceiptService.cs (simplified)
public class GemmaReceiptService : IGemmaReceiptService
{
    private readonly ILiteRtLmEngine _engine;       // ← from port (§6)
    private readonly IModelManager _modelManager;   // ← from port (§6)
    // ... receipt-specific prompt building and JSON parsing

    public async Task<GemmaReceiptResult?> ExtractReceiptAsync(
        byte[] imageBytes, string locale = "fi", CancellationToken ct = default)
    {
        if (!_engine.IsInitialized) return null;

        // Create a fresh conversation with receipt-specific system instruction
        _engine.CreateConversation(new ConversationOptions
        {
            SystemInstruction = BuildReceiptSystemPrompt(locale), // §7.2
            TopK = 1, Temperature = 0.0f // deterministic for structured extraction
        });

        var prompt = BuildReceiptPrompt(locale); // §7.3-7.4
        var result = await _engine.SendMessageAsync(prompt, imageBytes, ct);

        if (!result.Success) return null;
        return ParseGemmaJson(result.Text, result.ProcessingTimeMs);
    }
}
```

### 7.2 System Prompt

```
You are a receipt data extraction engine. You analyze receipt images and extract structured data as JSON.

RULES:
1. Extract ONLY what you can see in the image. Never fabricate data.
2. All monetary amounts use decimal notation (e.g., 12.50, not 12,50).
3. Dates in ISO 8601 format (YYYY-MM-DD).
4. If a field is not visible or unclear, set it to null.
5. For line items, extract each product/service as a separate entry.
6. VAT rates should be expressed as percentages (e.g., 25.5, not 0.255).
7. Currency should be ISO 4217 code (EUR, SEK, NOK, etc.).
8. Respond with ONLY valid JSON — no markdown, no explanation.
```

### 7.3 Structured Output Schema

The user prompt includes the expected output schema:

```
Extract all data from this receipt image as JSON matching this exact schema:

{
  "vendor_name": "string or null",
  "receipt_date": "YYYY-MM-DD or null",
  "total_amount": number_or_null,
  "subtotal_amount": number_or_null,
  "vat_amount": number_or_null,
  "currency": "EUR",
  "payment_method": "card|cash|mobile|null",
  "line_items": [
    {
      "description": "string",
      "quantity": number,
      "unit": "kpl|kg|m|l|null",
      "unit_price": number,
      "total_price": number,
      "vat_rate": number_or_null
    }
  ],
  "vat_breakdown": [
    {
      "rate": number,
      "taxable_amount": number,
      "vat_amount": number
    }
  ]
}
```

### 7.4 Finnish Receipt Specialization

Finnish receipts have unique characteristics. An additional context prompt:

```
FINNISH RECEIPT CONTEXT:
- Common VAT rates: 25.5% (general), 14% (food), 10% (books, transport, accommodation)
- Previous rates may appear: 24% (pre-2025 general), 13.5% (early 2025 food)
- "YHTEENSÄ" = Total, "VEROTON" = Tax-free/Subtotal, "ALV" = VAT
- "KPL" = pieces, "KG" = kilograms
- Finnish decimal separator is comma (12,50) but output as dot (12.50)
- Business ID format: 1234567-8
- Receipt may show: store name, address, business ID, timestamp, items, totals, VAT table, payment method
```

### 7.5 Multi-language Support

The locale parameter adjusts the context prompt:

| Locale | Additional Keywords |
|---|---|
| `fi` | YHTEENSÄ, VEROTON, ALV, KPL, KÄTEINEN, KORTTI |
| `sv` | TOTALT, MOMS, KONTANT, KORT |
| `en` | TOTAL, VAT, TAX, SUBTOTAL, CASH, CARD |
| `de` | GESAMT, MWST, BAR, KARTE |
| `et` | KOKKU, KÄIBEMAKS |
| `no` | TOTALT, MVA, KONTANT |
| `da` | TOTAL, MOMS, KONTANT |

### 7.6 Receipt DI Registration

```csharp
// MauiProgram.cs — receipt layer registration (Deliverable 2)
// Requires port registration (§6.9) to be present.

builder.Services.AddSingleton<IGemmaReceiptService, GemmaReceiptService>();
```

---

## 8. Data Model Changes

### 8.1 OcrExtraction Updates

The existing `OcrExtraction` model needs minimal changes:

```csharp
// Models/OcrExtraction.cs — additions

// New provider value
// Provider: "on_device", "azure", "google", "aws", "manual", "gemma_e2b", "gemma_e4b"

// New fields
public string? ExtractionMethod { get; set; }  // "regex_v6.8.1" | "gemma_vision" | "hybrid"
public string? ModelVersion { get; set; }       // "gemma-4-E2B-it-int4" | "gemma-4-E4B-it-int4"
public bool IsGemmaExtraction { get; set; }     // Quick flag for analytics
```

### 8.2 ExtractedLineItem Model

The existing `ExtractedLineItem` class (already referenced in `OcrExtraction.LineItemsJson`) needs to match the Gemma output:

```csharp
// Existing model — verify compatibility
public class ExtractedLineItem
{
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal? VatRate { get; set; }
    
    // New: Gemma confidence for this line item
    public double? Confidence { get; set; }
}
```

### SQLite Migration

```csharp
// AppDatabase.cs — add to InitAsync migration section
await _connection.ExecuteAsync(@"
    ALTER TABLE ocr_extractions ADD COLUMN extraction_method TEXT;
    ALTER TABLE ocr_extractions ADD COLUMN model_version TEXT;
    ALTER TABLE ocr_extractions ADD COLUMN is_gemma_extraction INTEGER DEFAULT 0;
");
```

---

## 9. Confidence Scoring & Validation

### 9.1 VAT Math Verification

Reuse the existing `VatZoneMath` engine from ReceiptParserService as a **post-validation** step:

```csharp
public class ReceiptValidator
{
    /// <summary>
    /// Validates Gemma extraction against Finnish VAT math rules.
    /// Returns adjusted confidence score.
    /// </summary>
    public ValidationResult ValidateExtraction(GemmaReceiptResult result)
    {
        var issues = new List<string>();
        double confidence = result.Confidence;
        
        // 1. Line items sum = subtotal?
        if (result.LineItems.Any() && result.SubtotalAmount.HasValue)
        {
            var lineTotal = result.LineItems.Sum(li => li.TotalPrice);
            var diff = Math.Abs(lineTotal - result.SubtotalAmount.Value);
            if (diff > 0.02m) // 2 cent tolerance
            {
                issues.Add($"Line items sum ({lineTotal}) ≠ subtotal ({result.SubtotalAmount})");
                confidence -= 0.15;
            }
        }
        
        // 2. Subtotal + VAT = total?
        if (result.SubtotalAmount.HasValue && result.VatAmount.HasValue && result.TotalAmount.HasValue)
        {
            var computed = result.SubtotalAmount.Value + result.VatAmount.Value;
            var diff = Math.Abs(computed - result.TotalAmount.Value);
            if (diff > 0.02m)
            {
                issues.Add($"Subtotal + VAT ({computed}) ≠ total ({result.TotalAmount})");
                confidence -= 0.20;
            }
        }
        
        // 3. VAT breakdown sums = total VAT?
        if (result.VatBreakdown.Any() && result.VatAmount.HasValue)
        {
            var breakdownVat = result.VatBreakdown.Sum(vb => vb.VatAmount);
            var diff = Math.Abs(breakdownVat - result.VatAmount.Value);
            if (diff > 0.05m)
            {
                issues.Add($"VAT breakdown sum ({breakdownVat}) ≠ total VAT ({result.VatAmount})");
                confidence -= 0.15;
            }
        }
        
        // 4. Known Finnish VAT rates?
        var knownRates = new[] { 25.5m, 24m, 14m, 13.5m, 10m, 0m };
        foreach (var item in result.LineItems.Where(li => li.VatRate.HasValue))
        {
            if (!knownRates.Contains(item.VatRate!.Value))
            {
                issues.Add($"Unknown VAT rate: {item.VatRate}%");
                confidence -= 0.05;
            }
        }
        
        return new ValidationResult
        {
            AdjustedConfidence = Math.Max(0, Math.Min(1, confidence)),
            Issues = issues,
            IsMathValid = !issues.Any(i => i.Contains("≠"))
        };
    }
}
```

### 9.2 Cross-Validation with Regex Parser

In **Hybrid mode**, both paths run in parallel. The results are cross-validated:

```csharp
public OcrExtraction CrossValidate(GemmaReceiptResult gemma, OcrExtraction legacy)
{
    // If both agree on total → high confidence
    if (gemma.TotalAmount.HasValue && legacy.TotalAmount > 0)
    {
        var diff = Math.Abs(gemma.TotalAmount.Value - legacy.TotalAmount);
        if (diff < 0.05m)
        {
            // Strong agreement — use Gemma (has line items)
            return MapToOcrExtraction(gemma, confidence: 0.95);
        }
    }
    
    // If Gemma VAT math validates and legacy doesn't → prefer Gemma
    var gemmaValidation = _validator.ValidateExtraction(gemma);
    if (gemmaValidation.IsMathValid && legacy.VatAmount == 0)
    {
        return MapToOcrExtraction(gemma, confidence: gemmaValidation.AdjustedConfidence);
    }
    
    // Otherwise: use legacy totals + Gemma line items
    var merged = MapToOcrExtraction(gemma);
    merged.TotalAmount = legacy.TotalAmount > 0 ? legacy.TotalAmount : gemma.TotalAmount ?? 0;
    merged.VatAmount = legacy.VatAmount > 0 ? legacy.VatAmount : gemma.VatAmount ?? 0;
    merged.ExtractionMethod = "hybrid";
    return merged;
}
```

### 9.3 Confidence Thresholds

| Threshold | Value | Action |
|---|---|---|
| Auto-accept | ≥ 0.90 | Populate Receipt fields, mark `IsConfirmed = false` |
| Review suggested | 0.60 – 0.89 | Populate fields, highlight yellow for user review |
| Manual entry | < 0.60 | Show "Could not parse receipt" — user enters manually |
| Legacy fallback | Model unavailable | ReceiptParserService v6.8.1 as-is |

---

## 10. Performance Budget

> **⚠️ PROVISIONAL ESTIMATES**: All numbers in this table are derived from Google's text-only benchmarks and published model card memory guidance. **None have been measured with our actual receipt workload** (multimodal image input, Kotlin bridge overhead, .NET interop layer, real device under normal app memory pressure). These targets will be baselined and adjusted during Deliverable 1 proof tasks (§6.10) and Deliverable 2 Phase 1 benchmarking.

| Metric | Target | E2B Estimate | E4B Estimate | Source / Confidence |
|---|---|---|---|---|
| Engine init (cold start) | < 10 seconds | ~3-5s | ~4-6s | Google docs say "significant time" — 10s budget is defensive | 
| Engine init (warm, with cache) | < 3 seconds | ~1-2s | ~2-3s | Assumes `cacheDir` works — unverified |
| Receipt extraction (image→JSON) | < 20 seconds | ~7-8s (text-only estimate) | ~17s (text-only estimate) | **UNVERIFIED for multimodal** — actual may differ significantly |
| Memory: model weights | — | ~3.2 GB (E2B INT4) | ~5 GB (E4B INT4) | Google model card — reasonable |
| Memory: peak during image inference | < 4.5 GB (E2B) | **Unknown** | **Unknown** | **Must measure**: model weights + image tensor + bridge overhead + app footprint |
| Memory: stable after 10 parses | No growth >100 MB | **Unknown** | **Unknown** | **Must measure**: repeated init/infer/dispose cycle |
| Model download | < 10 min on 4G | ~2.6 GB / ~5 min | ~3.7 GB / ~7 min | Simple bandwidth math — reliable |
| Battery impact per receipt | < 1% drain | Minimal (CPU, short task) | Minimal | Assumption — not yet measured |
| App cold start (no model load) | No impact | 0 ms (lazy load) | 0 ms (lazy load) | By design — reliable |

**Critical**: Model loading is **lazy** — the Gemma model is only loaded into memory when the user first captures a receipt (or explicitly in Settings). This ensures zero impact on app startup time.

**What "unknown" means here**: These cells represent quantities that cannot be reliably estimated from published data. The actual values depend on the interaction between model weights, image tensor allocation, Kotlin↔C# bridge memory copies, and Android's memory management behavior under pressure. They will be filled in during Deliverable 1 proof tasks and the Deliverable 2 benchmark harness.

---

## 11. Testing Strategy

### Unit Tests

```csharp
// Tests for ReceiptValidator
[TestCase("YHTEENSÄ 45.90, ALV 7.82, items sum 45.90", true)]    // Finnish receipt
[TestCase("Total mismatch: items 40.00, subtotal 45.00", false)]  // Math fail
[TestCase("Unknown VAT rate 19%", true)]                          // German receipt
public void ValidateExtraction_ReturnsExpectedResult(string scenario, bool mathValid)
```

### Integration Tests (on-device)

```markdown
1. **Golden Set**: 50+ real Finnish receipts (redacted PII) with manually verified totals/line items
   - S-Market, K-Supermarket, Prisma, Lidl, Tokmanni, Motonet, Biltema
   - Hardware stores: Stark, K-Rauta, Bauhaus
   - Building supplies, fuel, food receipts
   - Restaurants, hotels (14% / 10% VAT)

2. **Regression Set**: All receipts where v6.8.1 currently succeeds
   - Gemma must not regress on these

3. **Improvement Set**: Receipts where v6.8.1 fails on line items
   - Primary success metric for Gemma

4. **Edge Cases**:
   - Crumpled/folded receipts
   - Low-light photos
   - Tilted/rotated images
   - Multi-page receipts (split across photos)
   - Foreign receipts (Swedish, Estonian, German)
   - Faded thermal paper
```

### Accuracy Metrics

| Metric | v6.8.1 Baseline | Gemma Target | Measurement |
|---|---|---|---|
| **Total amount exact match** | ~85% | ≥ 90% | ±0.02€ of ground truth |
| **VAT amount exact match** | ~75% | ≥ 85% | ±0.05€ |
| **Vendor name** | ~70% | ≥ 80% | Fuzzy match ≥ 0.8 |
| **Receipt date** | ~80% | ≥ 90% | Exact date match |
| **Line item count** | ~40% | ≥ 70% | Correct number of items |
| **Line item amounts** | ~30% | ≥ 65% | Each item ±0.02€ |
| **Line item descriptions** | ~25% | ≥ 60% | Fuzzy match ≥ 0.7 |

---

## 12. Migration Path from v6.8.1

### Phase Approach — No Breaking Changes

```
Phase 1: Gemma runs in PARALLEL with legacy (Hybrid mode)
         └─ Legacy pipeline unchanged
         └─ Gemma results stored alongside for comparison
         └─ User sees legacy results; Gemma data used for analytics only

Phase 2: Gemma becomes PRIMARY with legacy validation
         └─ User sees Gemma results (with line items!)
         └─ Legacy runs silently for cross-validation
         └─ If Gemma confidence < 0.60 → fall back to legacy display

Phase 3: Gemma standalone + legacy as emergency fallback
         └─ Legacy code path maintained but only invoked on Gemma failure
         └─ ReceiptParserService kept for devices without Gemma model
```

### Settings UI

```xml
<!-- SettingsPage.xaml — new section -->
<StackLayout Padding="16" IsVisible="{Binding IsSmartReceiptAvailable}">
  <Label Text="{local:Translate SmartReceiptParsing}" 
         FontAttributes="Bold" FontSize="16" />
  <Label Text="{local:Translate SmartReceiptDescription}" 
         FontSize="12" TextColor="Gray" />
  
  <!-- Model download toggle -->
  <Grid ColumnDefinitions="*,Auto" Padding="0,8">
    <Label Text="{local:Translate DownloadAIModel}" VerticalOptions="Center" />
    <Switch IsToggled="{Binding IsGemmaModelDownloaded}" 
            Toggled="OnGemmaModelToggled" />
  </Grid>
  
  <!-- Download progress -->
  <ProgressBar Progress="{Binding GemmaDownloadProgress}" 
               IsVisible="{Binding IsGemmaDownloading}" />
  <Label Text="{Binding GemmaModelSizeText}" FontSize="11" TextColor="Gray" />
  
  <!-- Model preference -->
  <Picker Title="{local:Translate ReceiptParsingMode}"
          SelectedIndex="{Binding ReceiptParsingModeIndex}"
          IsVisible="{Binding IsGemmaModelDownloaded}">
    <Picker.Items>
      <x:String>Auto</x:String>
      <x:String>Always AI</x:String>
      <x:String>Always Classic</x:String>
    </Picker.Items>
  </Picker>
</StackLayout>
```

---

## 13. Risk Analysis

### 13.1 Risk Register

| # | Risk | Probability | Impact | Mitigation | Gate |
|---|---|---|---|---|---|
| **R1** | **Binding surface unusable** — `AndroidMavenLibrary` resolves the artifact but generated C# binding for `Engine`, `Conversation`, `Content`, `Backend`, and lifecycle APIs requires excessive `Metadata.xml` fixup or manual wrapper work | **High** | **Critical** | Kotlin bridge (`LiteRtLmBridge.kt`) as primary strategy. Budget full Week 1 for binding metadata. Fallback: C++ P/Invoke | G1 |
| **R2** | **Multimodal bridge failure** — Image-bearing `Content` objects cannot be constructed or passed through the Kotlin→C# interop layer, blocking all receipt parsing | **Medium-High** | **Critical** | Core Proof Task #2 (§6.10.1). If image input fails through bridge, entire receipt parsing goal is dead | G2 |
| **R3** | **Performance assumptions wrong** — All timing numbers in §3.3 and §10 are from text-only Kaggle benchmarks; real receipt image+JSON workload may be 2-5× slower | **Medium** | **High** | Every number marked PROVISIONAL. Measure actual receipt workload in Week 2. Latency envelope has room (budget is 15s, abort at 30s) | G3 |
| **R4** | **Memory pressure kills app** — Image inference peak RSS unknown; may exceed available RAM on mid-range devices or trigger Android low-memory killer | **Medium** | **High** | §13.4 measurement protocol. RAM gate before init. `Engine.Dispose()` after extraction. If peak exceeds device budget, fall back to smaller model or legacy OCR | G4 |
| **R5** | **JSON output unreliable** — Model produces malformed JSON, partial JSON, or hallucinated fields that break parsing | **Medium** | **High** | §13.3 deterministic fallback chain. `LlGuidanceConfig` constrained decoding if available through bridge. Gate question G5 measures consistency | G5 |
| **R6** | **Kotlin binding metadata requires manual fixup** | High | Medium | Known .NET Android Binding Library friction — budget Week 1 for `Metadata.xml` transforms | G1 |
| **R7** | **Maven dependency resolution issues** | Medium | Medium | Fallback: download AAR manually from Maven Central → `<AndroidLibrary>` include | G1 |
| **R8** | **E2B accuracy insufficient for receipts** | Medium | High | Fall back to E4B or Hybrid mode. Phase 1 accuracy gate catches this | — |
| **R9** | **Model too large for many user devices** | Medium | Medium | E2B (2.6 GB) as default; on-demand download; storage check before download | — |
| **R10** | **Inference too slow on mid-range phones** | Medium | Medium | Backend selection (GPU/NPU). Background task with progress UI. §10 latency budget | G3 |
| **R11** | **Gemma hallucinates line items** | Medium | High | VAT math cross-validation (§9.1). Confidence scoring. Hybrid mode fallback | G5 |
| **R12** | **iOS LiteRT-LM C++/Swift SDK binding** | High | Medium | **Out of scope for Deliverable 1.** iOS stays on legacy pipeline; Android-first rollout | — |
| **R13** | **Model weights become unavailable** | Low | Low | Self-host on SiteLedger CDN mirror | — |
| **R14** | **User privacy concerns** | Low | Low | All processing on-device; no data sent to cloud. Privacy-positive framing | — |
| **R15** | **LiteRT-LM SDK pre-1.0 API changes** | Medium | Medium | Pin to v0.10.x; isolate behind `ILiteRtLmEngine` interface; update on stable release | — |

> **Risk severity ranking**: R1 > R2 > R5 > R3 ≈ R4 > R8 > R11 > R6 > rest. The top 5 risks (R1-R5) map directly to gate questions G1-G5 and are resolved in Week 1-2 of Phase 0. If any of R1 or R2 materializes as unfixable, the project is dead — there is no workaround for "the binding doesn't work" or "images can't get through."

### 13.2 Binding Viability — What Is Actually Unknown

The core risk is **not** "can we download the Maven artifact?" That's a solved problem — `<AndroidMavenLibrary>` handles artifact resolution, and manual AAR download is a known fallback.

**The unknown is whether the generated binding surface is usable enough** to call the APIs we actually need:

| API Surface | What We Need | What Could Go Wrong |
|---|---|---|
| `Engine` + `EngineConfig` | Create engine, configure backend, set model path | Kotlin object patterns may generate unusable C# stubs |
| `Conversation` + `ConversationConfig` | Create conversation from engine, configure sampling | `SamplerConfig` builder pattern may not survive binding |
| `Content` (multimodal) | Construct content with image bytes + text prompt | Image byte array handling through JNI→C# interop is untested |
| `Backend` enum | Select GPU/CPU backend | Enum binding is usually reliable — low risk |
| Lifecycle (`close()`) | Deterministic disposal of native resources | `AutoCloseable` → `IDisposable` mapping is well-understood |
| Streaming (`Flow`) | Token-by-token output | Kotlin `Flow` **does not bind** to C# — bridge required regardless |

**Five questions we cannot answer from documentation alone:**

1. Does the generated binding expose `Engine.initialize()` with a usable signature, or is it mangled by Kotlin suspend/continuation translation?
2. Can `Content` objects with image byte arrays survive the Kotlin→JNI→C# boundary without corruption?
3. Does `Conversation.sendMessage()` (which returns `Flow<String>`) bind at all, or does the bridge need to collect the flow and return via callback?
4. Are there transitive Kotlin stdlib dependencies that break the binding build?
5. Does the `LlGuidanceConfig` API (constrained decoding) survive binding, or is it an internal-only class?

**Path 1: Kotlin/Maven + Binding Library (Primary)**
- `<AndroidMavenLibrary>` resolves `com.google.ai.edge.litertlm:litertlm-android:0.10.1`
- `LiteRtLmBridge.kt` wraps coroutine/Flow APIs into callback-based interface
- `Metadata.xml` transforms fix generated binding issues
- **Budget**: Full Week 1

**Path 2: C++ P/Invoke (Fallback)**
- Cross-platform but complex — especially for multimodal `Content` construction (image byte arrays through P/Invoke)
- Only pursued if Path 1 fails gate question G1
- **Budget**: Would add 2-3 weeks — significant schedule impact

> **Google provides zero .NET binding guidance.** There are no official samples, no community wrappers, no Stack Overflow threads. We are the first to attempt this. This is precisely why the port (§6) is a separate deliverable with its own acceptance gate.

### 13.3 Structured Output Fallback Chain

JSON reliability is not a binary pass/fail — it's a spectrum from "perfect JSON" to "complete garbage." The fallback chain must be **deterministic** (no random retries hoping for different output) and **degradation-aware** (each step is strictly worse than the previous):

```
Step 1: PARSE — Direct JSON.parse() on model output
        ├─ Success → return parsed ReceiptData
        └─ Failure → Step 2

Step 2: EXTRACT — Strip markdown fences, find outermost { }, retry parse
        ├─ Success → return parsed ReceiptData
        └─ Failure → Step 3

Step 3: CONSTRAINED RETRY — Re-infer with LlGuidanceConfig (if available)
        ├─ LlGuidanceConfig available → enforce JSON schema, parse result
        │   ├─ Success → return parsed ReceiptData
        │   └─ Failure → Step 4
        └─ LlGuidanceConfig NOT available → skip to Step 4

Step 4: SIMPLIFIED RETRY — Re-infer with minimal schema (totals only, no line items)
        ├─ Success → return partial ReceiptData (totalAmount, vatAmount, vendor)
        └─ Failure → Step 5

Step 5: LEGACY FALLBACK — Fall back to v6.8.1 OCR + regex pipeline
        └─ Always succeeds (at legacy accuracy level)
```

**Key principle**: Steps 3-5 are **fallback, not normal operation.** If gate question G5 shows that Step 1 or Step 2 fails more than 20% of the time with the chosen model, that's a red flag — either the prompt needs redesign or the model isn't suitable.

**Implementation**:

```csharp
// GemmaReceiptService — fallback chain
private async Task<ReceiptParseResult> ParseWithFallback(string modelOutput, byte[] imageBytes)
{
    // Step 1: Direct parse
    if (TryParseJson<ReceiptData>(modelOutput, out var result))
        return ReceiptParseResult.FromGemma(result, confidence: 0.95);

    // Step 2: Extract JSON block
    var extracted = ExtractJsonBlock(modelOutput);
    if (extracted != null && TryParseJson<ReceiptData>(extracted, out result))
        return ReceiptParseResult.FromGemma(result, confidence: 0.85);

    // Step 3: Constrained decoding retry (if available)
    if (_engine.SupportsConstrainedDecoding)
    {
        var constrained = await _engine.InferWithSchemaAsync(imageBytes, ReceiptJsonSchema);
        if (TryParseJson<ReceiptData>(constrained, out result))
            return ReceiptParseResult.FromGemma(result, confidence: 0.80);
    }

    // Step 4: Simplified schema retry
    var simplified = await _engine.InferAsync(imageBytes, SimplifiedPrompt);
    if (TryParseJson<ReceiptTotalsOnly>(simplified, out var totals))
        return ReceiptParseResult.FromGemmaPartial(totals, confidence: 0.60);

    // Step 5: Legacy fallback
    return await _legacyParser.ParseAsync(imageBytes);
}
```

### 13.4 Memory Pressure Measurement Protocol

Android's low-memory killer operates on RSS (Resident Set Size), not virtual memory. "The model card says 2.6 GB" tells us the **file size**, not the **runtime memory footprint**. The following measurements must be taken on the actual target device during Phase 0:

| # | Measurement | Method | Pass Condition |
|---|---|---|---|
| **M1** | Baseline app RSS (no model loaded) | `Debug.getNativeHeapSize()` + `/proc/self/status` VmRSS | Record value (expected ~80-120 MB) |
| **M2** | RSS after `Engine.initialize()` completes | Same as M1, measured after init callback | Delta from M1 < 1.5× model file size |
| **M3** | **Peak RSS during image inference** | Sample `/proc/self/status` every 100ms during `SendMessage` with image input | **Peak < 4.0 GB** on 8 GB device (50% of physical RAM) |
| **M4** | RSS after `Engine.Dispose()` | Same as M1, measured 5s after dispose | Returns to within 20% of M1 baseline |
| **M5** | RSS ratchet across 10 init/infer/dispose cycles | Run 10 full cycles, record RSS after each dispose | No monotonic increase > 10% from cycle 1 to cycle 10 |
| **M6** | `ComponentCallbacks2.onTrimMemory()` behavior | Register callback, observe during full inference cycle | App must **never** receive `TRIM_MEMORY_CRITICAL` during normal operation |

> **M3 is the critical measurement.** If peak RSS during image inference pushes total app memory past the low-memory killer threshold, the app will be terminated mid-inference. This is not a "degraded experience" — it's a crash. The model card RAM number (2.6 GB file ≈ similar runtime footprint for quantized models) is a starting point, but actual peak during multimodal inference with image decoding could be significantly higher.

**If M3 fails**: Investigate E2B INT4 quantized variant (smaller footprint), reduce image resolution before passing to model, or gate feature behind high-RAM device check (`ActivityManager.getMemoryInfo()`).

---

## 14. Implementation Phases

```
┌──────────────────────────────────────────────────────────────────────┐
│                         TIMELINE OVERVIEW                            │
│                                                                      │
│  Phase 0 ─── LiteRT-LM Port ──── 4 weeks ───── GATE ──┐             │
│                                                         │             │
│  Phase 1 ─── Receipt Prototype ── 2 weeks ──────────────┤             │
│  Phase 2 ─── Integration ──────── 2 weeks ──────────────┤             │
│  Phase 3 ─── Production ──────── 1-2 weeks ─────────────┤             │
│  Phase 4 ─── iOS + LoRA ──────── TBD ───────────────────┘             │
│                                                                      │
│  Total: 9-12 weeks (Phase 0 must complete before Phase 1 starts)     │
└──────────────────────────────────────────────────────────────────────┘
```

### Phase 0: LiteRT-LM .NET MAUI Port (4 weeks) — DELIVERABLE 1

> **This phase is the standalone deliverable defined in §6. No receipt logic. No prompt engineering. Pure infrastructure.**

| Week | Tasks | Output | Tests |
|---|---|---|---|
| **0.1** | Configure `<AndroidMavenLibrary>` for LiteRT-LM. Create `LiteRtLm.NET.Android` binding project. Resolve Kotlin binding metadata transforms. Build `LiteRtLmBridge.kt` | Maven artifact binds, project compiles | G1 |
| **0.2** | Implement `AndroidLiteRtLmEngine` + `ModelManager`. Download E2B model. Engine init + **text inference** (Proof Task #1). Begin **image inference** (Proof Task #2) | Engine initializes, text + image generation works | G1, G2, G3 |
| **0.3** | Streaming. Backend selection (GPU/CPU). Concurrency. `DeviceCapability`. `NullLiteRtLmEngine`. **Memory measurement** (§13.4, Proof Task #3). Structured output testing | Memory safe across 10 cycles, JSON output consistent | G4, G5 |
| **0.4** | API polish, XML docs, edge case hardening. Full test suite (T1-T11). **Run acceptance gate** (§6.10) | All 3 proof tasks pass. G1-G5 answered. AC-S1 through AC-S7 verified | Full gate |

**Effort**: ~80 person-hours  
**Exit**: **GATE DECISION** (see §6.10)  
**Deliverable**: `LiteRtLm.NET` — open-source library, published to NuGet.org

---

### Phase 1: Receipt Prototype & Validate (2 weeks) — DELIVERABLE 2 START

> **Blocked until Phase 0 gate passes.**

| Task | Effort | Owner |
|---|---|---|
| Build `GemmaReceiptService` on top of `ILiteRtLmEngine` (§7.1) | 2 days | Mobile dev |
| Prompt engineering with 20 test receipts (§7.2-7.4) | 3 days | AI/Mobile |
| JSON response parsing + `GemmaReceiptResult` mapping | 1 day | Mobile dev |
| Run accuracy benchmarks on golden set | 2 days | QA |
| Decision gate: proceed / abandon / pivot | 1 day | Team |

**Success criteria for Phase 1:**
- Total amount extraction ≥ 85% accuracy on Finnish receipts
- Line item extraction ≥ 50% accuracy (any improvement over v6.8.1's ~30%)
- Inference completes in < 20 seconds on Samsung S24+

### Phase 2: Integration & Hybrid Mode (2 weeks)

| Task | Effort |
|---|---|
| Integrate `IGemmaReceiptService` into ExpenseEditorViewModel | 2 days |
| Model download UI in SettingsPage | 2 days |
| Hybrid mode — parallel extraction + strategy selection (§4.2) | 2 days |
| Cross-validation logic (§9.2) | 1 day |
| VAT math post-validation (§9.1) | 1 day |
| Localization (14 languages for new UI strings) | 1 day |

### Phase 3: Production Rollout (1-2 weeks)

| Task | Effort |
|---|---|
| Golden set regression testing (50+ receipts) | 3 days |
| Beta release to internal testers | 2 days |
| Telemetry: Gemma vs legacy accuracy tracking | 1 day |
| Settings preference persistence | 0.5 days |
| Documentation & ADR | 0.5 days |

### Phase 4: iOS + Optimization (TBD — when LiteRT-LM Swift is stable)

| Task | Effort |
|---|---|
| iOS native framework binding (`LiteRtLm.NET.iOS`) | 3 days |
| iOS `iOSLiteRtLmService` implementation | 2 days |
| iOS port acceptance tests (mirror T1-T9 for iOS) | 2 days |
| LoRA fine-tuning on Finnish receipt dataset | 3-5 days |

**Total estimated effort:** 9-12 weeks (Phase 0 must complete before Phase 1 starts)

---

## 15. Cost Analysis

### Development Costs

| Item | Estimate | Deliverable |
|---|---|---|
| **Phase 0: LiteRT-LM Port** | **~80 person-hours** | **Deliverable 1** |
| Phase 1: Receipt Prototype | ~40 person-hours | Deliverable 2 |
| Phase 2: Integration | ~60 person-hours | Deliverable 2 |
| Phase 3: Production | ~30 person-hours | Deliverable 2 |
| Phase 4: iOS (deferred) | ~50 person-hours | Both (port + receipts) |
| **Total** | **~260 person-hours** | |

### Runtime Costs

| Item | Cost |
|---|---|
| Model inference | ✅ **Free** — 100% on-device |
| Model hosting (HuggingFace) | ✅ **Free** — open download |
| CDN mirror (optional) | ~€5/month (Cloudflare R2 egress) |
| Cloud OCR fallback (existing) | Unchanged — only used when Gemma unavailable |
| **Per-receipt cost** | **€0.00** |

### Comparison with Cloud AI Alternatives

| Service | Per-receipt cost | Offline | Line items |
|---|---|---|---|
| **Gemma 4 on-device** | **€0.00** | **✅** | **✅** |
| Google Cloud Document AI | ~€0.01-0.05 | ❌ | ✅ |
| AWS Textract | ~€0.01-0.02 | ❌ | ✅ |
| Azure Form Recognizer | ~€0.01-0.03 | ❌ | ✅ |
| OpenAI GPT-4o (via API) | ~€0.01-0.02 | ❌ | ✅ |

**Key advantage**: Gemma on-device is the **only** solution that is both **free** and **fully offline**.

---

## 16. Future Extensions

### 16.1 LoRA Fine-Tuning for Finnish Receipts

Once Phase 1 validates the base model, a LoRA adapter trained on Finnish receipt data could significantly boost accuracy:

```python
# Fine-tuning dataset structure
{
    "image": "receipt_001.jpg",
    "prompt": "Extract all data from this receipt as JSON...",
    "completion": { "vendor_name": "S-Market Kallio", "total_amount": 45.90, ... }
}
```

- Training data: 500-1000 annotated Finnish receipts
- LoRA rank: 8-16
- Training cost: ~$10-50 on Google Colab
- Expected improvement: +5-15% accuracy on Finnish receipt specifics

### 16.2 Real-Time Camera Preview

Instead of capture → process, overlay extraction hints on the live camera feed:
- "Hold steady — reading receipt..."
- Auto-capture when text quality threshold is met
- Requires streaming inference (feasible at E2B's 47 tok/s)

### 16.3 Receipt Categorization

Extend the prompt to also classify the receipt:
- Material purchase → auto-suggest linking to a work entry
- Fuel → auto-populate travel expenses
- Food → apply correct VAT rate (14% for groceries, 25.5% for restaurants)
- Hardware store → suggest material creation

### 16.4 Multi-Receipt Batch Processing

Process all unprocessed receipts in batch during idle time:
- Background task when phone is charging + on Wi-Fi (model download)
- Queue-based processing with priority (most recent first)

### 16.5 Voice + Receipt Combined

Integration with existing MVA (Magic Voice Assistant):
- "I bought 15 meters of copper pipe at Stark" + receipt photo
- Cross-reference voice description with Gemma line item extraction
- Auto-create expense + material in one flow

---

## Appendix A: Gemma 4 Model Comparison Matrix

| Feature | E2B | E4B | 31B | 26B MoE |
|---|---|---|---|---|
| On-device mobile | ✅ Primary target | ✅ High-end only | ❌ Server only | ❌ Server only |
| Total parameters | ~9.6B | ~15B | 31B | 26B |
| Effective parameters | 2B | 4B | 31B | 4B active |
| Model size (INT4) | 2.58 GB | 3.65 GB | 17.4 GB | 15.6 GB |
| RAM required (INT4) | ~3.2 GB | ~5 GB | ~17.4 GB | ~15.6 GB |
| Context window | 128K | 128K | 256K | 256K |
| Image input | ✅ | ✅ | ✅ | ✅ |
| Audio input | ✅ | ✅ | ❌ | ❌ |
| DocVQA (estimated) | ~73+ | ~78+ | ~88+ | ~85+ |
| License | Apache 2.0 | Apache 2.0 | Apache 2.0 | Apache 2.0 |
| Android (LiteRT-LM) | ✅ Stable | ✅ Stable | ❌ | ❌ |
| iOS (LiteRT-LM) | 🚧 In Dev | 🚧 In Dev | ❌ | ❌ |

## Appendix B: Existing Code Dependencies

| File | Lines | Relevance | Changes Needed |
|---|---|---|---|
| `Services/IReceiptServices.cs` | ~50 | Interface definitions | Add `IGemmaReceiptService` |
| `Services/ReceiptCaptureService.cs` | 792 | Image preprocessing | **None** — reuse as-is |
| `Services/OcrService.cs` | ~200 | Plugin.Maui.OCR wrapper | **None** — legacy path |
| `Services/ReceiptParserService.cs` | 7,094 | Regex parser v6.8.1 | **None** — legacy fallback |
| `Models/OcrExtraction.cs` | ~60 | Extraction result model | Add 3 fields (§8.1) |
| `Models/Receipt.cs` | ~40 | Receipt entity | **None** |
| `Data/ExpenseRepository.cs` | ~300 | Expense/receipt SQLite | **None** |
| `Data/AppDatabase.cs` | 1,478 | SQLite wrapper | Add migration for 3 columns |
| `ViewModels/ExpenseEditorViewModel.cs` | ~400 | Receipt UI logic | Wire `IGemmaReceiptService` |
| `Pages/SettingsPage.xaml(.cs)` | ~300 | Settings UI | Add model download section |
| `MauiProgram.cs` | ~150 | DI registration | Add Gemma services |

## Appendix C: Localization Keys Required

```
SmartReceiptParsing = "Smart Receipt Parsing" / "Älykäs kuitintunnistus"
SmartReceiptDescription = "Use AI to extract line items from receipts (requires ~3 GB download)" / "Käytä tekoälyä kuitin rivitietojen tunnistamiseen (vaatii ~3 GB latauksen)"
DownloadAIModel = "Download AI Model" / "Lataa tekoälymalli"
AIModelDownloading = "Downloading AI model..." / "Ladataan tekoälymallia..."
AIModelReady = "AI model ready" / "Tekoälymalli valmis"
AIModelSize = "Model size: {0}" / "Mallin koko: {0}"
ProcessingWithAI = "Analyzing receipt with AI..." / "Analysoidaan kuittia tekoälyllä..."
ReceiptParsingMode = "Receipt parsing mode" / "Kuitintunnistustapa"
ReceiptParsingAuto = "Auto" / "Automaattinen"
ReceiptParsingAlwaysAI = "Always AI" / "Aina tekoäly"
ReceiptParsingAlwaysClassic = "Always Classic" / "Aina perinteinen"
```

All 14 languages (en, fi, sv, no, da, de, fr, es, pt, it, et, lv, lt, sk) required before production release.
