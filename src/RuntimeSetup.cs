using System;
using System.Runtime.Versioning;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace LayerUnpacker
{
    public static class RuntimeSetup
    {
        public static void Initialize()
        {
            // Apply before any file/path API is used. A copied standalone EXE
            // must not rely on an adjacent .config for correct path handling.
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        }
    }
}
