using System.Runtime.CompilerServices;

namespace MPlusKeybot.Tests;

internal static class SqliteProviderInitializer
{
	[ModuleInitializer]
	internal static void Initialize() => SQLitePCL.Batteries_V2.Init();
}
