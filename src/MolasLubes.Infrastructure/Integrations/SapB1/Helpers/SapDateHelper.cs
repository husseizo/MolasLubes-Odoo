using System;
using System.Collections.Generic;
using System.Text;

namespace MolasLubes.Infrastructure.Integrations.SapB1.Helpers;

public static class SapDateHelper
{
    private static readonly DateTime SqlMinDate = new(1753, 1, 1);
    private static readonly DateTime SafeBaseline = new(2000, 1, 1);

    public static DateTime Normalize(DateTime input)
    {
        if (input < SqlMinDate)
            return SafeBaseline;

        return input;
    }

    public static string ToSqlDate(DateTime input)
    {
        return Normalize(input).ToString("yyyy-MM-dd");
    }
}