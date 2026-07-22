using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CRM.Domain.Common;

/// <summary>
/// Lightweight guard clauses for validating arguments and invariants at method boundaries.
/// Each method fails fast with a descriptive <see cref="ArgumentException"/> (or a derived type)
/// instead of letting an invalid value propagate into an obscure <see cref="NullReferenceException"/>
/// or bad state further down the call stack.
/// </summary>
/// <remarks>
/// Parameter names are captured automatically via <see cref="CallerArgumentExpressionAttribute"/>,
/// so the common case is simply <c>_db = Guard.AgainstNull(db);</c>.
/// </remarks>
public static class Guard
{
    /// <summary>Throws <see cref="ArgumentNullException"/> when <paramref name="value"/> is null; otherwise returns it.</summary>
    public static T AgainstNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName);
        return value;
    }

    /// <summary>Throws <see cref="ArgumentNullException"/> when the nullable value type has no value; otherwise returns the underlying value.</summary>
    public static T AgainstNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : struct
    {
        if (value is null)
            throw new ArgumentNullException(paramName);
        return value.Value;
    }

    /// <summary>Throws when <paramref name="value"/> is null or the empty string; otherwise returns it.</summary>
    public static string AgainstNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty.", paramName);
        return value;
    }

    /// <summary>Throws when <paramref name="value"/> is null, empty, or only whitespace; otherwise returns it.</summary>
    public static string AgainstNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        return value;
    }

    /// <summary>Throws when <paramref name="value"/> is an empty <see cref="Guid"/>; otherwise returns it.</summary>
    public static Guid AgainstEmpty(
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Value cannot be an empty GUID.", paramName);
        return value;
    }

    /// <summary>Throws when <paramref name="value"/> equals <c>default(T)</c>; otherwise returns it.</summary>
    public static T AgainstDefault<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(value, default))
            throw new ArgumentException($"Value cannot be the default {typeof(T).Name}.", paramName);
        return value;
    }

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="value"/> is less than zero; otherwise returns it.</summary>
    public static T AgainstNegative<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : struct, IComparable<T>
    {
        if (value.CompareTo(default) < 0)
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        return value;
    }

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="value"/> is less than or equal to zero; otherwise returns it.</summary>
    public static T AgainstNegativeOrZero<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : struct, IComparable<T>
    {
        if (value.CompareTo(default) <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be greater than zero.");
        return value;
    }

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="value"/> falls outside the inclusive [min, max] range; otherwise returns it.</summary>
    public static T AgainstOutOfRange<T>(
        T value, T min, T max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : struct, IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be between {min} and {max} (inclusive).");
        return value;
    }

    /// <summary>Throws when the enum <paramref name="value"/> is not a defined member of its type; otherwise returns it.</summary>
    public static TEnum AgainstUndefinedEnum<TEnum>(
        TEnum value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(paramName, value, $"Value is not a defined {typeof(TEnum).Name}.");
        return value;
    }

    /// <summary>Throws an <see cref="ArgumentException"/> with <paramref name="message"/> when <paramref name="condition"/> is true.</summary>
    public static void Against(bool condition, string message, string? paramName = null)
    {
        if (condition)
            throw new ArgumentException(message, paramName);
    }
}
