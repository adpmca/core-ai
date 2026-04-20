namespace Diva.Agents.Hooks;

using System.Collections.Frozen;
using Diva.Core.Models;

/// <summary>
/// Startup-built registry mapping hook class names to their Types.
/// Replaces fragile AppDomain.GetAssemblies() scanning at runtime.
/// Built once during DI registration, then used for O(1) lookups.
/// </summary>
public sealed class HookTypeRegistry
{
    private readonly FrozenDictionary<string, Type> _map;

    public HookTypeRegistry(IEnumerable<Type> hookTypes)
    {
        _map = hookTypes
            .Where(t => typeof(IAgentLifecycleHook).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToFrozenDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
    }

    public Type? Resolve(string className) =>
        _map.GetValueOrDefault(className);

    public IReadOnlyList<string> RegisteredHookNames => [.. _map.Keys];

    /// <summary>
    /// Scan assemblies once at startup and build the registry.
    /// Called from Program.cs during DI registration.
    /// </summary>
    public static HookTypeRegistry BuildFromAssemblies(params System.Reflection.Assembly[] assemblies)
    {
        var types = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return []; }
            })
            .Where(t => typeof(IAgentLifecycleHook).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        return new HookTypeRegistry(types);
    }
}
