using Agentic.Core;

namespace Agentic.Abstractions;

public interface IModelProvider
{
    IAgentModel CreateModel();
}
