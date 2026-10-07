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

    [TestMethod]
    public void HidValueUsageRangesExpandOneReportedFieldPerUsage()
    {
        bool supported = WindowsHidDeviceEnumerator.TryGetScalarUsageIds(
            isRange: true, usageMinimum: 0x30, usageMaximum: 0x37, reportCount: 2, out ushort[] usageIds);

        Assert.IsTrue(supported);
        CollectionAssert.AreEqual(new ushort[] { 0x30, 0x31 }, usageIds);
    }

    [TestMethod]
    public void HidRepeatedSingleUsageArrayRemainsUnsupported()
    {
        bool supported = WindowsHidDeviceEnumerator.TryGetScalarUsageIds(
            isRange: false, usageMinimum: 0x30, usageMaximum: 0x30, reportCount: 2, out ushort[] usageIds);

        Assert.IsFalse(supported);
        Assert.AreEqual(0, usageIds.Length);
    }

    [TestMethod]
    public void HidValueUsageExpansionIsBounded()
    {
        bool supported = WindowsHidDeviceEnumerator.TryGetScalarUsageIds(
            isRange: true, usageMinimum: 0, usageMaximum: byte.MaxValue, reportCount: 129, out ushort[] usageIds);

        Assert.IsFalse(supported);
        Assert.AreEqual(0, usageIds.Length);
    }
}
