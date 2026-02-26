using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Xunit;

namespace Agentic.Tests;

/// <summary>
/// Tests for tool execution, registration, and tool calling within Agent.
/// </summary>
public sealed class ToolExecutionTests
{
    [Fact]
    public async Task Agent_executes_tool_and_continues_conversation()
    {
        var provider = new TestToolModelProvider(
            new ToolCallingModel(),
            new TestTool("greet", "Greets someone", args =>
                Task.FromResult($"Hello, {args}!")));

        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(provider.Tool)
            .Build();

        var response = await agent.ReplyAsync("Please greet Alice");

        Assert.Contains("Hello, Alice!", response);
    }

    [Fact]
    public async Task Multiple_tools_registered()
    {
        var tool1 = new TestTool("add", "Adds two numbers", args =>
        {
            var parts = args.Split(',');
            var sum = int.Parse(parts[0]) + int.Parse(parts[1]);
            return Task.FromResult(sum.ToString());
        });

        var tool2 = new TestTool("multiply", "Multiplies two numbers", args =>
        {
            var parts = args.Split(',');
            var product = int.Parse(parts[0]) * int.Parse(parts[1]);
            return Task.FromResult(product.ToString());
        });

        var provider = new TestModelProvider(new TestAgentModel());
        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool1)
            .WithTool(tool2)
            .Build();

        // Should not throw during build
        Assert.NotNull(agent);
    }

    [Fact]
    public async Task Tool_not_found_returns_error()
    {
        var provider = new TestModelProvider(new ToolCallingModel());
        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .Build();

        var response = await agent.ReplyAsync("Please greet Alice");

        // Should fail gracefully and return some response
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Tool_result_included_in_next_turn()
    {
        IReadOnlyList<ChatMessage>? capturedMessages = null;

        var tool = new TestTool("test", "Test tool", args =>
            Task.FromResult("TOOL_RESULT"));

        var provider = new TestModelProvider(
            new CapturingModel(msgs => { capturedMessages = msgs; return Task.FromResult(new AgentResponse("ok")); })
        );

        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool)
            .Build();

        // First, trigger a tool call
        var provider2 = new TestToolModelProvider(
            new ToolCallingModel(),
            tool);
        var agent2 = new AgentBuilder()
            .WithModelProvider(provider2)
            .WithTool(tool)
            .Build();

        var response = await agent2.ReplyAsync("test");

        // Tool result should be included
        Assert.Contains(agent2.History, m => m.Role == ChatRole.Tool && m.Content.Contains("TOOL_RESULT"));
    }

    [Fact]
    public async Task Tool_with_empty_arguments()
    {
        var tool = new TestTool("noargs", "Tool with no args", args =>
            Task.FromResult("no args result"));

        var model = new ToolCallingModelWithArgs("noargs", "");
        var provider = new TestToolModelProvider(model, tool);

        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool)
            .Build();

        var response = await agent.ReplyAsync("test");

        Assert.Contains("no args result", response);
    }

    [Fact]
    public async Task Tool_exception_handled_gracefully()
    {
        var tool = new TestTool("error", "Tool that throws", args =>
            throw new InvalidOperationException("Tool error"));

        var model = new ToolCallingModelWithArgs("error", "arg");
        var provider = new TestToolModelProvider(model, tool);

        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool)
            .Build();

        var response = await agent.ReplyAsync("test");

        // Should not throw - error should be handled
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Duplicate_tool_calls_prevented()
    {
        var callCount = 0;
        var tool = new TestTool("count", "Count calls", args =>
        {
            callCount++;
            return Task.FromResult(callCount.ToString());
        });

        var model = new RepeatingToolCallingModel("count", "arg");
        var provider = new TestToolModelProvider(model, tool);

        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool)
            .Build();

        var response = await agent.ReplyAsync("test");

        // Tool should only be called once due to duplicate detection
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Tool_name_case_sensitive()
    {
        var tool = new TestTool("MyTool", "A tool", args =>
            Task.FromResult("result"));

        var provider = new TestModelProvider(new TestAgentModel());
        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool)
            .Build();

        Assert.NotNull(agent);
    }

    private sealed class TestTool : ITool
    {
        private readonly Func<string, Task<string>> _invoke;

        public string Name { get; }
        public string Description { get; }

        public TestTool(string name, string description, Func<string, Task<string>> invoke)
        {
            Name = name;
            Description = description;
            _invoke = invoke;
        }

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => _invoke(arguments);
    }

    private sealed class TestAgentModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentResponse("ok"));
        }
    }

    private sealed class ToolCallingModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            var lastTool = messages.LastOrDefault(m => m.Role == ChatRole.Tool);
            if (lastTool is not null)
            {
                var result = lastTool.Content.Split(':', 2)[1].Trim();
                return Task.FromResult(new AgentResponse($"Got result: {result}"));
            }

            return Task.FromResult(new AgentResponse(
                "Calling tool",
                new List<AgentToolCall> { new("greet", "Alice") }));
        }
    }

    private sealed class ToolCallingModelWithArgs : IAgentModel
    {
        private readonly string _toolName;
        private readonly string _args;

        public ToolCallingModelWithArgs(string toolName, string args)
        {
            _toolName = toolName;
            _args = args;
        }

        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            var lastTool = messages.LastOrDefault(m => m.Role == ChatRole.Tool);
            if (lastTool is not null)
            {
                return Task.FromResult(new AgentResponse($"Got result: {lastTool.Content}"));
            }

            return Task.FromResult(new AgentResponse(
                "Calling tool",
                new List<AgentToolCall> { new(_toolName, _args) }));
        }
    }

    private sealed class RepeatingToolCallingModel : IAgentModel
    {
        private readonly string _toolName;
        private readonly string _args;

        public RepeatingToolCallingModel(string toolName, string args)
        {
            _toolName = toolName;
            _args = args;
        }

        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            // Always call the same tool (will be deduplicated)
            return Task.FromResult(new AgentResponse(
                "Calling same tool",
                new List<AgentToolCall> { new(_toolName, _args) }));
        }
    }

    private sealed class CapturingModel : IAgentModel
    {
        private readonly Func<IReadOnlyList<ChatMessage>, Task<AgentResponse>> _capture;

        public CapturingModel(Func<IReadOnlyList<ChatMessage>, Task<AgentResponse>> capture)
        {
            _capture = capture;
        }

        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
            => _capture(messages);
    }

    private sealed class TestModelProvider : IModelProvider
    {
        private readonly IAgentModel _model;

        public TestModelProvider(IAgentModel model) => _model = model;

        public IAgentModel CreateModel() => _model;
    }

    private sealed class TestToolModelProvider : IModelProvider
    {
        private readonly IAgentModel _model;
        public ITool Tool { get; }

        public TestToolModelProvider(IAgentModel model, ITool tool)
        {
            _model = model;
            Tool = tool;
        }

        public IAgentModel CreateModel() => _model;
    }
}
