namespace BioTwin_AI.BlazorClient.Components;

public sealed class OutlineTreeItem(int level, string title)
{
    public int Level { get; } = level;

    public string Title { get; } = title;

    public List<OutlineTreeItem> Children { get; } = [];
}
