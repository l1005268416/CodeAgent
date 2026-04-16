using Xunit;
using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;
using CodeAgent.Core.Sessions;
using CodeAgent.Core.Tools;
using CodeAgent.Core.Context;

namespace CodeAgent.Core.Tests;

public class ModelTests
{
    [Fact]
    public void Session_CanBeCreated()
    {
        var session = new Session
        {
            Name = "Test Session"
        };

        Assert.NotNull(session.Id);
        Assert.Equal("Test Session", session.Name);
        Assert.NotNull(session.Messages);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public void Message_CanBeCreated()
    {
        var message = new Message
        {
            Role = MessageRole.User,
            Content = "Hello"
        };

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("Hello", message.Content);
    }

    [Fact]
    public void ToolResult_CanBeCreated()
    {
        var result = new ToolResult
        {
            Success = true,
            Content = "File content"
        };

        Assert.True(result.Success);
        Assert.Equal("File content", result.Content);
    }

    [Fact]
    public void ToolDefinition_CanBeCreated()
    {
        var tool = new ToolDefinition
        {
            Name = "test_tool",
            Description = "A test tool"
        };

        Assert.Equal("test_tool", tool.Name);
        Assert.Equal("A test tool", tool.Description);
    }
}

public class ContextManagerTests
{
    private class TestLogger : ILogger<ContextManager>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact]
    public void EstimateTokenCount_ReturnsApproximateCount()
    {
        var contextManager = new ContextManager(new TestLogger());
        
        var tokenCount = contextManager.EstimateTokenCount("Hello World");
        
        Assert.Equal(3, tokenCount);
    }

    [Fact]
    public void BuildMessages_AddsUserMessage()
    {
        var contextManager = new ContextManager(new TestLogger());
        var session = new Session();

        var messages = contextManager.BuildMessages(session, "Test message");

        Assert.Single(messages);
        Assert.Equal("Test message", messages[0].Content);
        Assert.Equal(MessageRole.User, messages[0].Role);
    }
}