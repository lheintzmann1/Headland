using System.Globalization;
using System.Runtime.CompilerServices;

namespace FarmSim.Core.Tests;

internal static class TestSetup
{
    /// <summary>The game formats numbers with the invariant culture (the game does the same at startup).</summary>
    [ModuleInitializer]
    public static void Init()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
