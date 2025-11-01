# GitHub Copilot Instructions for DistributedNonce

## Project Overview

DistributedNonce is a .NET library implementing distributed Nonce service for block chain using Redis as the backend. It provides a robust solution for coordinating access to Nonce across multiple processes or services in distributed systems.

## Code Organization

```
src/DistributedLockManager/
├── Services/           # Implementation classes
└── Configuration.cs    # DI setup
```
