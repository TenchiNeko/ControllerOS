using System.Reflection;
using System.Runtime.ExceptionServices;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Output;

namespace ControllerOS.Windows;

/// <summary>Submits normalized output snapshots through HIDMaestro's Xbox 360 virtual gamepad.</summary>
public sealed class VirtualXboxOutput : IOutputSink, IDisposable
{
    private readonly IOutputSink output;
    private readonly IDisposable lifetime;

    private VirtualXboxOutput(IOutputSink output, IDisposable lifetime)
    {
        this.output = output;
        this.lifetime = lifetime;
    }

    /// <summary>
    /// Starts the virtual gamepad. First use installs HIDMaestro's signed driver
    /// package, which requires an elevated administrator process.
    /// </summary>
    public static VirtualXboxOutput Create()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Xbox virtual output backend is available only on Windows.");

        string dependencyPath = Path.Combine(AppContext.BaseDirectory, "HIDMaestro.Core.dll");
        if (!File.Exists(dependencyPath))
            throw new InvalidOperationException("HIDMaestro.Core.dll is missing. Rebuild or reinstall ControllerOS with its pinned Windows virtual-output dependency.");

        try
        {
            Type backendType = typeof(VirtualXboxOutput).Assembly.GetType("ControllerOS.Windows.HidMaestroVirtualOutput", throwOnError: true)!;
            object backend = Activator.CreateInstance(backendType, nonPublic: true)!;
            return new VirtualXboxOutput((IOutputSink)backend, (IDisposable)backend);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        catch (Exception error) when (IsBackendError(error))
        {
            throw new InvalidOperationException(
                $"Could not load the HIDMaestro Xbox virtual output. Confirm the pinned backend files are present and the application is running as administrator. Details: {error.Message}",
                error);
        }
    }

    public void Commit(TimeSpan timestamp, OutputState state) => output.Commit(timestamp, state);
    public void Reset(TimeSpan timestamp) => output.Reset(timestamp);
    public void Dispose() => lifetime.Dispose();

    private static bool IsBackendError(Exception error) => error is
        FileNotFoundException or DllNotFoundException or BadImageFormatException or TypeLoadException or
        MissingMethodException or InvalidOperationException;
}
