using System;
using System.Collections.Generic;
using System.Text;

namespace MolasLubes.Infrastructure.Common;

public static class DateTimeUtcExtensions
{
    public static DateTime AsUtc(this DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public static DateTime? AsUtc(this DateTime? value)
    {
        return value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
    }
}
