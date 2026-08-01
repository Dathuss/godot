using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Godot.NET.Sdk.Tasks;

internal static class TaskExtensions
{
    public static bool HasMetadata(this ITaskItem taskItem, string name) => ((ICollection<string>)taskItem.MetadataNames).Contains(name);

    public static bool TryGetMetadata(this ITaskItem taskItem, string name, out string? value)
    {
        value = ((IEnumerable<string>)taskItem.MetadataNames).FirstOrDefault(x => x == name);
        return value != null;
    }

    public static bool GetBoolMetadata(this ITaskItem taskItem, string name)
    {
        return taskItem.GetMetadata(name).Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static TaskItem CloneWithNewItemSpec(this ITaskItem originTaskItem, string itemSpec)
    {
        TaskItem copy = new(itemSpec);
        originTaskItem.CopyMetadataTo(copy);
        return copy;
    }
}
