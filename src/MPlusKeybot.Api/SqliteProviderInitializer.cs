using System.Runtime.CompilerServices;

internal static class SqliteProviderInitializer
{
	[ModuleInitializer]
	internal static void Initialize() => SQLitePCL.Batteries_V2.Init();
}
