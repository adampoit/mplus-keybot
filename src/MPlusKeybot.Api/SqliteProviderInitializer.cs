using System.Runtime.CompilerServices;

namespace MPlusKeybot.Api;

internal static class SqliteProviderInitializer
{
	[ModuleInitializer]
	internal static void Initialize() => SQLitePCL.Batteries_V2.Init();
}
