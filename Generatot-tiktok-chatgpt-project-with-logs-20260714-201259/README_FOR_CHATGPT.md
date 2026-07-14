# ChatGPT Project Reading Guide

This ZIP is a compact source-and-debug snapshot for the TikTok short generator project.
It intentionally excludes build artifacts, obj/bin binaries, local IDE files, audio WAV files, downloaded videos, rendered MP4 files, and bundled tools.

## Read first
1. README.md
2. source/TikTokGenerator/Services/ShortGenerator.cs
3. source/TikTokGenerator/Services/ScriptService.cs
4. source/TikTokGenerator/Services/QualityGateService.cs
5. source/TikTokGenerator/Services/SourceAnalysisDiagnosticsService.cs
6. source/TikTokGenerator/Services/TrendService.cs
7. source/TikTokGenerator/Services/ContentBriefService.cs
8. tests/TikTokGenerator.Tests/*.cs

## Logs
The logs/ directory contains recent generation runs. For each run, inspect:
- debug/debug.log
- debug/topic.json
- debug/source-analysis.json
- debug/script-normalized.json
- debug/script-after-content-review-repair.json
- debug/content-review.json
- debug/quality-gate.json
- debug/voice-analysis.json
- debug/clip-analysis.json
- project.json and script.json if present

## Useful commands in the original project
- dotnet test TikTokGenerator.Tests/TikTokGenerator.Tests.csproj -c CodexTest
- dotnet build TikTokGenerator/TikTokGenerator.csproj -c Debug

## Context
The recent work focused on making the pipeline robust across categories and topics:
- source analysis and sanitization,
- thematic brief generation,
- source-safe script repair,
- content-review sanitization,
- quality gate behavior,
- richer offline seed source texts,
- regression tests for problems that should not return.
