using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Input;

public interface IInputSource
{
    ControllerCapabilities Capabilities { get; }
    IEnumerable<ControllerInputEvent> ReadEvents();
}
