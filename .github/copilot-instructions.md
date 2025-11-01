# GitHub Copilot Instructions for DistributedLockManager

## Project Overview

DistributedLockManager is a .NET library implementing distributed locking using Redis as the backend. It provides a robust solution for coordinating access to resources across multiple processes or services in distributed systems.

## Code Organization

```
src/DistributedLockManager/
├── Interfaces/          # Contract definitions
├── Services/           # Implementation classes
└── Configuration.cs    # DI setup
```

## Key Components

### Interfaces

```csharp
// For void operations
public interface IDistributedLockService
{
    Task RunWithLockAsync(
        Func<Task> func, 
        string key, 
        CancellationToken cancellationToken, 
        int expiryInSecond = 30, 
        int waitInSecond = 10, 
        int retryInSecond = 1
    );
}

// For operations with return values
public interface IDistributedLockService<TResult>
{
    Task<TResult> RunWithLockAsync(
        Func<Task<TResult>> func, 
        string key, 
        CancellationToken cancellationToken, 
        int expiryInSecond = 30, 
        int waitInSecond = 10, 
        int retryInSecond = 1
    );
}
```

## Common Patterns

### Lock Key Generation
- Format: `{entity-type}:{id}:{operation}`
- Examples:
  ```csharp
  "user:123:profile"
  "order:456:processing"
  "inventory:789:update"
  ```

### Default Timings
```csharp
expiryInSecond = 30;  // Lock duration
waitInSecond = 10;    // Acquisition timeout
retryInSecond = 1;    // Retry interval
```

### Error Handling
```csharp
try
{
    await lockService.RunWithLockAsync(
        async () => await ProcessData(),
        "process:123",
        CancellationToken.None
    );
}
catch (InvalidOperationException ex) 
{
    // Lock acquisition failed
}
```

### Dependency Injection
```csharp
services.AddDistributedLockManager();
```

## Best Practices

1. **Lock Scoping**
   ```csharp
   // GOOD: Specific lock key
   "user:123:profile-update"
   
   // BAD: Too broad
   "user-operations"
   ```

2. **Error Handling**
   ```csharp
   // GOOD: Specific catch
   catch (InvalidOperationException ex) when (ex.Message.Contains("Resource is locked"))
   
   // BAD: Generic catch
   catch (Exception ex)
   ```

3. **Cancellation Support**
   ```csharp
   // GOOD: Pass cancellation token
   await lockService.RunWithLockAsync(task, key, cancellationToken);
   
   // BAD: Use None
   await lockService.RunWithLockAsync(task, key, CancellationToken.None);
   ```

## Testing Patterns

1. **Lock Acquisition**
   ```csharp
   [Fact]
   public async Task ShouldAcquireLock()
   {
       // Arrange
       var key = "test:123";
       
       // Act
       await lockService.RunWithLockAsync(...);
       
       // Assert
       // Verify operation completed
   }
   ```

2. **Concurrent Access**
   ```csharp
   [Fact]
   public async Task ShouldHandleConcurrentAccess()
   {
       var tasks = Enumerable.Range(0, 5)
           .Select(_ => lockService.RunWithLockAsync(...));
       
       await Task.WhenAll(tasks);
   }
   ```

## Common Extensions

1. **Custom Lock Options**
   ```csharp
   public class LockOptions
   {
       public int ExpiryInSecond { get; set; }
       public int WaitInSecond { get; set; }
       public int RetryInSecond { get; set; }
   }
   ```

2. **Operation Result Wrapper**
   ```csharp
   public class LockResult<T>
   {
       public bool IsSuccess { get; set; }
       public T Result { get; set; }
       public string Error { get; set; }
   }
   ```

## Documentation Standards

1. XML Comments for Public APIs
   ```csharp
   /// <summary>
   /// Executes the specified task within a distributed lock.
   /// </summary>
   /// <param name="key">The lock key</param>
   /// <returns>A task representing the asynchronous operation</returns>
   public Task RunWithLockAsync(...)
   ```

2. Example Documentation
   ```csharp
   // Example:
   // var result = await lockService.RunWithLockAsync(
   //     async () => await GetUserProfile(123),
   //     "user:123:profile",
   //     cancellationToken
   // );
   ```