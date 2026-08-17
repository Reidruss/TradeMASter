namespace TradeMASter.Agents.Prompts;

public class CommonPrompt(string name, string prompt, string? refName = null)
{
    public string? RefName { get; init; } = refName;
    public string Name { get; init; } = name;
    public string Prompt { get; init; } = prompt;
}

