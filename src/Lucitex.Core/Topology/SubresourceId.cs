namespace Lucitex.Core.Topology;

public readonly record struct SubresourceId(
    int Part,
    int ArrayElement,
    int Face,
    LevelKey Level);
