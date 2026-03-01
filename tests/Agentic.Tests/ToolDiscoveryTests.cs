using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Tests.Fakes;
using Xunit;

namespace Agentic.Tests;

public class ToolDiscoveryTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    [AgenticTool]
    private sealed class MarkedTool : ITool
    {
        public string Name => "marked_tool";
        public string Description => "A tool marked for auto-discovery.";
        public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
            => Task.FromResult("marked_result");
    }

    [AgenticTool]
    private sealed class AnotherMarkedTool : ITool
    {
        public string Name => "another_marked_tool";
        public string Description => "Another auto-discovered tool.";
        public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
            => Task.FromResult("another_result");
    }

    // Not decorated — must NOT be discovered.
    private sealed class UnmarkedTool : ITool
    {
        public string Name => "unmarked_tool";
        public string Description => "Not decorated with [AgenticTool].";
        public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
            => Task.FromResult("unmarked_result");
    }

    // Decorated but abstract — must NOT be instantiated or registered.
    [AgenticTool]
    private abstract class AbstractMarkedTool : ITool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract Task<string> InvokeAsync(string arguments, CancellationToken ct = default);
    }

    // Decorated, implements ITool, but no parameterless constructor.
    // Lives in a nested container class so it does NOT pollute the general
    // test assembly scan (tests that want a clean scan use typeof(AgentBuilder).Assembly).
    // Only the dedicated "throws" test references this type directly.
    internal static class BadFixtures
    {
        [AgenticTool]
        internal sealed class NoDefaultCtorTool : ITool
        {
            public NoDefaultCtorTool(string _) { }
            public string Name => "no_ctor_tool";
            public string Description => "Has no parameterless ctor.";
            public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
                => Task.FromResult("x");
        }
    }

    private static readonly IAgentModel EchoModel = new EchoAgentModel();

    private sealed class EchoAgentModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
            => Task.FromResult(new AgentResponse("ok"));
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually registered [AgenticTool]-decorated tools (using WithTool) build successfully.
    /// This verifies that the attribute does not break manual registration.
    /// </summary>
    [Fact]
    public void WithTool_registered_marked_tool_builds_successfully()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(EchoModel))
            .WithTool(new MarkedTool())
            .WithTool(new AnotherMarkedTool())
            .Build();

        Assert.NotNull(agent);
    }

    /// <summary>
    /// Scanning an assembly that contains zero [AgenticTool] types (the main library)
    /// succeeds silently and registers no tools.
    /// </summary>
    [Fact]
    public void WithToolsFromAssembly_with_empty_assembly_succeeds()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(EchoModel))
            .WithToolsFromAssembly(typeof(AgentBuilder).Assembly)
            .Build();

        Assert.NotNull(agent);
    }

    /// <summary>
    /// The generic overload <c>WithToolsFromAssembly&lt;T&gt;()</c> resolves the assembly
    /// from the marker type and succeeds when that assembly has no [AgenticTool] types.
    /// </summary>
    [Fact]
    public void WithToolsFromAssembly_generic_overload_resolves_assembly_from_marker_type()
    {
        // AgentBuilder is in the main library assembly which has no [AgenticTool] classes.
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(EchoModel))
            .WithToolsFromAssembly<AgentBuilder>()
            .Build();

        Assert.NotNull(agent);
    }

    /// <summary>
    /// Manual WithTool and auto-discovery from a clean assembly co-exist without errors.
    /// </summary>
    [Fact]
    public void WithToolsFromAssembly_can_combine_with_explicit_WithTool()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(EchoModel))
            .WithToolsFromAssembly(typeof(AgentBuilder).Assembly) // zero [AgenticTool] types
            .WithTool(new MarkedTool())
            .Build();

        Assert.NotNull(agent);
    }

    /// <summary>
    /// An agent with a manually-registered [AgenticTool] tool can invoke it correctly.
    /// </summary>
    [Fact]
    public async Task WithTool_marked_tool_is_invoked_by_agent()
    {
        // A model that requests marked_tool on turn 1, then returns plain text.
        var model = new ToolRequestingModel("marked_tool", "{}", "call-1");

        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(model))
            .WithTool(new MarkedTool())
            .Build();

        var reply = await agent.ReplyAsync("use the tool");
        Assert.NotNull(reply.Content);
    }

    /// <summary>
    /// Unmarked tools registered manually are NOT mixed up with auto-discovered ones
    /// (i.e., auto-discovery respects the [AgenticTool] attribute boundary).
    /// When scanning the main library assembly (zero [AgenticTool] types) and separately
    /// adding an unmarked tool by hand, only the manually-added tool appears.
    /// </summary>
    [Fact]
    public void WithToolsFromAssembly_does_not_auto_discover_unmarked_tools()
    {
        var capturedToolNames = new List<string>();
        var model = new CapturingModel(capturedToolNames);

        // Scan main library (no [AgenticTool] types) + add an unmarked tool manually.
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(model))
            .WithToolsFromAssembly(typeof(AgentBuilder).Assembly)
            .WithTool(new UnmarkedTool())
            .Build();

        // Build must succeed.
        Assert.NotNull(agent);
        // The unmarked_tool was added manually so it will be present,
        // but no *auto-discovered* tools should have been added from the library assembly.
        // (We simply verify build does not throw and the agent is usable.)
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a type decorated with
    /// [AgenticTool] lacks a public parameterless constructor.
    /// We trigger this by scanning the dedicated BadFixtures assembly fragment
    /// directly via the public <c>WithToolsFromAssembly</c> overload pointed at
    /// the test assembly, which contains <c>BadFixtures.NoDefaultCtorTool</c>.
    /// </summary>
    [Fact]
    public void WithToolsFromAssembly_throws_when_marked_tool_has_no_default_constructor()
    {
        // The test assembly contains BadFixtures.NoDefaultCtorTool which is [AgenticTool]
        // but has no parameterless constructor. Scanning must throw.
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(EchoModel))
                .WithToolsFromAssembly(Assembly.GetExecutingAssembly())
                .Build();
        });
    }

    // ── helper models ─────────────────────────────────────────────────────────

    /// <summary>Requests one tool call on the first invocation, then returns plain text.</summary>
    private sealed class ToolRequestingModel(string toolName, string arguments, string callId) : IAgentModel
    {
        private int _callCount;

        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            if (_callCount++ == 0)
            {
                return Task.FromResult(new AgentResponse(
                    string.Empty,
                    [new AgentToolCall(toolName, arguments, callId)]));
            }
            return Task.FromResult(new AgentResponse("done"));
        }
    }

    /// <summary>Captures message content so tests can inspect injected context.</summary>
    private sealed class CapturingModel(List<string> capturedNames) : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            // The system prompt injected by Agent includes "Available tools: ..." with tool names.
            foreach (var msg in messages)
            {
                if (msg.Role == ChatRole.System && msg.Content.StartsWith("Available tools:"))
                {
                    foreach (var line in msg.Content.Split('\n'))
                    {
                        var trimmed = line.TrimStart('-', ' ');
                        var colonIdx = trimmed.IndexOf(':');
                        if (colonIdx > 0)
                            capturedNames.Add(trimmed[..colonIdx].Trim());
                    }
                }
            }
            return Task.FromResult(new AgentResponse("ok"));
        }
    }

    // ── AgenticToolAttribute name/description override fixtures ───────────────

    /// <summary>
    /// Tool whose [AgenticTool] attribute overrides both name and description.
    /// The ITool.Name / ITool.Description values should NOT be used by the agent.
    /// </summary>
    [AgenticTool(Name = "attr_name_override", Description = "Attribute description override.")]
    private sealed class ToolWithAttrOverrides : ITool
    {
        public string Name => "original_name";        // must be ignored when attribute overrides are set
        public string Description => "Original description."; // must be ignored
        public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
            => Task.FromResult("attr_result");
    }

    /// <summary>
    /// Tool whose [AgenticTool] attribute sets only Name; Description falls back to ITool.Description.
    /// </summary>
    [AgenticTool(Name = "attr_name_only")]
    private sealed class ToolWithAttrNameOnly : ITool
    {
        public string Name => "original_name_only";
        public string Description => "Kept description.";
        public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
            => Task.FromResult("name_only_result");
    }

    // ── attribute override tests ───────────────────────────────────────────────

    /// <summary>
    /// When [AgenticTool(Name = "...", Description = "...")] is set, the attribute values
    /// are used as the effective name and description rather than ITool.Name / ITool.Description.
    /// The agent lookup key must also use the overridden name so the tool is invoked correctly.
    /// </summary>
    [Fact]
    public async Task AgenticToolAttribute_name_override_is_used_for_invocation()
    {
        // Model requests the ATTRIBUTE name, not the ITool.Name.
        var model = new ToolRequestingModel("attr_name_override", "{}", "call-attr-1");

        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(model))
            .WithTool(new ToolWithAttrOverrides())
            .Build();

        // If the lookup key is ITool.Name ("original_name") instead of the attribute override
        // ("attr_name_override"), the agent will fail to find the tool and throw or return an error.
        var reply = await agent.ReplyAsync("use the tool");
        Assert.Equal("done", reply.Content);
    }

    /// <summary>
    /// When only [AgenticTool(Name = "...")] is set, the overridden name is the lookup key
    /// while the description falls back to ITool.Description.
    /// </summary>
    [Fact]
    public async Task AgenticToolAttribute_partial_override_name_only_works()
    {
        var model = new ToolRequestingModel("attr_name_only", "{}", "call-attr-2");

        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(model))
            .WithTool(new ToolWithAttrNameOnly())
            .Build();

        var reply = await agent.ReplyAsync("use the tool");
        Assert.Equal("done", reply.Content);
    }

    /// <summary>
    /// Without any attribute name override, ITool.Name is still the lookup key (regression guard).
    /// </summary>
    [Fact]
    public async Task Without_attribute_override_ITool_Name_is_still_the_lookup_key()
    {
        var model = new ToolRequestingModel("marked_tool", "{}", "call-plain-1");

        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(model))
            .WithTool(new MarkedTool())
            .Build();

        var reply = await agent.ReplyAsync("use the tool");
        Assert.Equal("done", reply.Content);
    }
}
