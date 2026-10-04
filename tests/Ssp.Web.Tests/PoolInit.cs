using System.Runtime.CompilerServices;

namespace Ssp.Web.Tests;

static class PoolInit
{
    // HACK: A blocked pool grows about one thread every half second, so a debounce timer misses its wait on a loaded runner.
    // The module initializer runs before any test class, so no class starves the pool first.
    [ModuleInitializer]
    internal static void Run() => ThreadPool.SetMinThreads(256, 256);
}
