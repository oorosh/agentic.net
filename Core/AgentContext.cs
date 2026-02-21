namespace Agentic.Core;

public sealed class AgentContext
{
    public AgentContext(string input, IReadOnlyList<ChatMessage> history)
    {
        Input = input;
        History = history;
        WorkingMessages = new List<ChatMessage>(history)
        {
            new(ChatRole.User, input)
        };
    }

    public string Input { get; }
    public IReadOnlyList<ChatMessage> History { get; }
    public IList<ChatMessage> WorkingMessages { get; }
}
