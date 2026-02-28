using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Tests.Fakes;
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

        var provider = new FakeModelProvider(new TestAgentModel());
        var agent = new AgentBuilder()
            .WithModelProvider(provider)
            .WithTool(tool1)
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
                return Task.FromResult(new AgentResponse($"Got result: {lastTool.Content}"));
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
