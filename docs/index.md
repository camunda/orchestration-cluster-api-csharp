# Camunda C# SDK — API Documentation

Welcome to the API reference for the **Camunda 8 Orchestration Cluster API** C# SDK.

## Quick Links

- [API Reference](api/index.md)
- [Getting Started](articles/getting-started.md)
- [GitHub Repository](https://github.com/camunda/orchestration-cluster-api-csharp)
- [NuGet Package](https://www.nuget.org/packages/Camunda.Client)

## Overview

This SDK provides a typed C# client for the Camunda 8 REST API, auto-generated from the upstream OpenAPI specification with ergonomic wrappers including:

- **Unified configuration** from environment variables (`CAMUNDA_*`)
- **OAuth and Basic authentication** with automatic token management
- **HTTP retry** with exponential backoff and jitter
- **Backpressure management** with adaptive concurrency
- **Eventual consistency** polling for search endpoints
