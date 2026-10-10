namespace HelloPos.Kernel;

/// <summary>
/// Marker for the shared kernel assembly. The kernel holds primitives shared by the API and the web
/// back office (Money and pricing from roadmap #10); it must not depend on any web or UI framework.
/// </summary>
public static class KernelAssembly;
