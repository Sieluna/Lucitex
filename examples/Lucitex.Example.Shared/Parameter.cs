using System.Globalization;

namespace Lucitex.Example.Shared;

public sealed record Choice(string Value, string Label);

public sealed record ParameterCondition(string Id, string Value);

public enum ParameterKind { Integer, Boolean, Choice }

public sealed record Parameter(string Id, string Label, string Description, ParameterKind Kind, string DefaultValue,
    int? Minimum = null, int? Maximum = null, IReadOnlyList<Choice>? Choices = null, ParameterCondition? Condition = null);

internal sealed record Field<T>(string Id, Func<T, Parameter> Describe, Func<T, string, T> Apply)
{
    internal Field<T> When(string id, string value) => this with {
        Describe = options => Describe(options) with { Condition = new(id, value) },
    };

    internal static Field<T> Integer(string id, string label, string description, int min, int max,
        Func<T, int> get, Func<T, int, T> set) => new(id,
        options => new(id, label, description, ParameterKind.Integer, get(options).ToString(CultureInfo.InvariantCulture), min, max),
        (options, text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? set(options, value)
            : throw new ArgumentException($"Parameter '{id}' requires an integer between {min} and {max}."));

    internal static Field<T> Boolean(string id, string label, string description, Func<T, bool> get, Func<T, bool, T> set) => new(id,
        options => new(id, label, description, ParameterKind.Boolean, get(options) ? "true" : "false"),
        (options, text) => bool.TryParse(text, out var value)
            ? set(options, value)
            : throw new ArgumentException($"Parameter '{id}' requires true or false."));

    internal static Field<T> Select<TValue>(string id, string label, string description, Func<T, TValue> get, Func<T, TValue, T> set,
        params (string Id, string Label, TValue Value)[] choices)
    {
        var byId = choices.ToDictionary(choice => choice.Id, choice => choice.Value, StringComparer.OrdinalIgnoreCase);
        var labels = Array.AsReadOnly(choices.Select(choice => new Choice(choice.Id, choice.Label)).ToArray());
        return new(id,
            options => new(id, label, description, ParameterKind.Choice,
                choices.First(choice => EqualityComparer<TValue>.Default.Equals(choice.Value, get(options))).Id, Choices: labels),
            (options, text) => byId.TryGetValue(text, out var value)
                ? set(options, value)
                : throw new ArgumentException($"Parameter '{id}' requires one of: {string.Join(", ", byId.Keys)}."));
    }
}
