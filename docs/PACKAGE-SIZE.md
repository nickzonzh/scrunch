# Package size

Measured on 22 September 2026 against the font-settings build (`363c96c`).
Sizes below use decimal MB; the payload guard uses binary MiB.

| Artifact | Before | After | Reduction |
| --- | ---: | ---: | ---: |
| Installer | 73,141,554 bytes | 49,493,811 bytes | 32.3% |
| Portable ZIP | 108,522,863 bytes | 73,254,496 bytes | 32.5% |
| Extracted payload | 281,295,253 bytes | 180,762,305 bytes | 35.7% |
| Payload files | 535 | 479 | 56 files |

## Shipping choices

Scrunch still bundles .NET and the Windows App SDK. Installation and portable
launch do not require the user to install a shared runtime.

The project references the WinUI and DWrite component packages from the same
Windows App SDK 1.8.6 release previously used. Their Base, Foundation and
InteractiveExperiences dependencies remain. The umbrella/runtime packages, AI,
ML, widgets and tensor dependency are gone. This uses the SDK's component build
targets and generated native manifests; no DLLs are manually deleted after build.
The removed files account for 50,659,074 bytes, including ONNX Runtime and DirectML.
Component packages are supported by the [Windows App SDK 1.8 release](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8).

ReadyToRun is disabled. The app and managed SDK projections ship IL without an
additional precompiled native-code copy. The .NET runtime's own supplied binaries
are retained. Fonts, paper bakes, shaders, text rendering and all product code
remain unchanged. Updated NuGet lockfiles and generated dependency notices match
the narrower graph.

`tools/verify-payload.ps1` rejects the unused AI/ML/widgets binaries and a payload
over 200 MiB (209,715,200 bytes). The current payload is 172.39 MiB. Review and
measure intentional increases rather than silently raising the budget.

## Startup tradeoff

Five alternating isolated launches of each Release build on Windows 10 x64 build
19045, using a fresh empty synthetic notebook for each process:

| Observation | Before | After |
| --- | ---: | ---: |
| Median launch to New note control available | 391 ms | 438 ms |
| Median first New note invocation to editor available | 266 ms | 308 ms |
| First observed launch in the sequence | 504 ms | 1,389 ms |

These are UI-automation timings, including process/command and polling overhead.
The first sample is not a controlled cold-cache benchmark. Four subsequent
candidate launches took 437–449 ms; the first-launch cost is retained here rather
than hidden by the median. This is one machine and a short sample, not a universal
performance guarantee. The candidate's managed assembly hash matches the final
packaged assembly. Raw evidence: ignored `artifacts/size-startup-comparison.json`.

## Why trimming remains off

A full trimming experiment reduced the extracted candidate to approximately
94 MB, but native startup failed with `No such interface supported`. The linker
also reported unresolved reflection/generic interop warnings in Windows SDK,
WinRT and SharpGen code. That candidate was rejected. The final build does not
suppress those warnings or ship the experimental source generator/serializer
changes. A future trimming or Native AOT project needs separate interop work and
full native acceptance; the 94 MB experiment is not a usable release.

## Verification

See [release verification](RELEASE-VERIFICATION.md) for exact artifact hashes,
the headless and native results, and the current platform coverage limits.
The full release script runs locked restores, headless checks, notice generation
and payload validation before creating the installer and ZIP. Both final packages
were exercised locally; actual Release menu discard was recorded and inspected.
