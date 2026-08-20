using System.Runtime.InteropServices;
using System.Windows;

// Where WPF looks for theme resource dictionaries. This app ships no generic.xaml and no
// per-theme dictionaries — the Fluent dictionary is merged in App.xaml — so both lookups are
// declared for what they are rather than left to be inferred.
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// Restricts P/Invoke resolution to System32. Every native call in this assembly targets
// user32.dll, which is a KnownDLL and therefore already mapped from a protected section rather
// than searched for — but this ships as a single .exe that people drop into their Downloads
// folder and run, and the attribute costs one line to make the search path explicit anyway.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
