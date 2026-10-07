using System.Reflection;
using System.Runtime.InteropServices;
using ControllerOS.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class WindowsHidInteropLayoutTests
{
    [TestMethod]
    public void HidCapabilityStructuresMatchTheWindowsSdkLayout()
    {
        Type enumerator = typeof(WindowsHidDeviceEnumerator);
        Type buttonCaps = enumerator.GetNestedType("HidpButtonCaps", BindingFlags.NonPublic)!;
        Type valueCaps = enumerator.GetNestedType("HidpValueCaps", BindingFlags.NonPublic)!;

        Assert.AreEqual(72, Marshal.SizeOf(buttonCaps));
        Assert.AreEqual(72, Marshal.SizeOf(valueCaps));
    }
}
