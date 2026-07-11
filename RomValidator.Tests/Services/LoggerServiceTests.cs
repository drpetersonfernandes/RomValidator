using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests.Services;

public class LoggerServiceTests
{
    [Fact]
    public void LogErrorDoesNotThrow()
    {
        // Act & Assert - should not throw even when logger not initialized (uses Log.Logger.None)
        var exception = Record.Exception(() => LoggerService.LogError("TestComponent", "Test error message"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogWarningDoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => LoggerService.LogWarning("TestComponent", "Test warning message"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogInfoDoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => LoggerService.LogInfo("TestComponent", "Test info message"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogExceptionDoesNotThrow()
    {
        // Act & Assert
        var testException = new InvalidOperationException("Test exception");
        var exception = Record.Exception(() => LoggerService.LogException("TestComponent", testException, "Test context"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogExceptionWithNullContextDoesNotThrow()
    {
        // Act & Assert
        var testException = new ArgumentNullException("param");
        var exception = Record.Exception(() => LoggerService.LogException("TestComponent", testException));
        Assert.Null(exception);
    }

    [Fact]
    public void LogDebugDoesNotThrow()
    {
        // Act & Assert - should not throw even in RELEASE mode (Conditional attribute handles this)
        var exception = Record.Exception(() => LoggerService.LogDebug("TestComponent", "Test debug message"));
        Assert.Null(exception);
    }

    [Fact]
    public void LoggerServiceAllowsAnyComponentName()
    {
        // Act & Assert
        var ex1 = Record.Exception(() => LoggerService.LogInfo(null!, "test"));
        var ex2 = Record.Exception(() => LoggerService.LogInfo(string.Empty, "test"));
        var ex3 = Record.Exception(() => LoggerService.LogInfo(new string('x', 1000), "test"));

        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.Null(ex3);
    }
}
