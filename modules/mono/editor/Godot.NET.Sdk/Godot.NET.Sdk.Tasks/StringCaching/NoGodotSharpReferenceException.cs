using System;
using Mono.Cecil;

namespace Godot.NET.Sdk.Tasks.StringCaching;

public class NoGodotSharpReferenceExeption(ModuleDefinition module) : Exception
{
    public override string Message { get; } = $"Module {module} does not contain a reference to GodotSharp.";
}
