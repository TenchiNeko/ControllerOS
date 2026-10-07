using ControllerOS.Core.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class ControllerProfileJsonTests
{
    [TestMethod]
    public void ProfileJsonRoundTripsStableMetadataAndValidatedSource()
    {
        var profile = new ControllerProfile(
            ControllerProfile.CurrentSchemaVersion,
            new ControllerProfileMetadata("simple", "A. User", "A test profile"),
            "on press(SOUTH):\n    press(EAST)\n");

        string first = ControllerProfileJson.Serialize(profile);
        ControllerProfile loaded = ControllerProfileJson.Deserialize(first);

        Assert.AreEqual(profile, loaded);
        Assert.AreEqual(first, ControllerProfileJson.Serialize(loaded));
    }

    [TestMethod]
    public void ProfileJsonExplainsUnsupportedVersionsAndRejectsAmbiguousSchema()
    {
        ControllerProfileFormatException future = Assert.ThrowsExactly<ControllerProfileFormatException>(() =>
            ControllerProfileJson.Deserialize("{\"schemaVersion\":2,\"metadata\":{},\"source\":\"\"}"));
        Assert.AreEqual("unsupported_schema_version", future.Code);
        StringAssert.Contains(future.Message, "supports version 1");

        ControllerProfileFormatException duplicate = Assert.ThrowsExactly<ControllerProfileFormatException>(() =>
            ControllerProfileJson.Deserialize("{\"schemaVersion\":1,\"schemaVersion\":1,\"metadata\":{},\"source\":\"\"}"));
        Assert.AreEqual("invalid_profile", duplicate.Code);

        ControllerProfileFormatException unknown = Assert.ThrowsExactly<ControllerProfileFormatException>(() =>
            ControllerProfileJson.Deserialize("{\"schemaVersion\":1,\"metadata\":{\"name\":\"x\"},\"source\":\"\",\"extra\":true}"));
        StringAssert.Contains(unknown.Message, "unsupported property 'extra'");
    }

    [TestMethod]
    public void ProfileJsonRejectsMalformedAndInvalidControllerScript()
    {
        ControllerProfileFormatException malformed = Assert.ThrowsExactly<ControllerProfileFormatException>(() =>
            ControllerProfileJson.Deserialize("{"));
        Assert.AreEqual("invalid_json", malformed.Code);

        var invalidScript = new ControllerProfile(
            ControllerProfile.CurrentSchemaVersion,
            new ControllerProfileMetadata("broken"),
            "on press(LEFT_STICK_X):\n    press(EAST)\n");
        ControllerProfileFormatException invalid = Assert.ThrowsExactly<ControllerProfileFormatException>(() =>
            ControllerProfileJson.Serialize(invalidScript));
        Assert.AreEqual("invalid_script", invalid.Code);
        StringAssert.Contains(invalid.Message, "requires a button control");
    }

    [TestMethod]
    public void ProfileFileLoadAndSaveUseBoundedUtf8AndPreserveExistingFileOnInvalidProfile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"controlleros-{Guid.NewGuid():N}.profile.json");
        var profile = new ControllerProfile(
            ControllerProfile.CurrentSchemaVersion,
            new ControllerProfileMetadata("file profile"),
            "on press(SOUTH):\n    press(EAST)\n");
        try
        {
            ControllerProfileJson.SaveFile(path, profile);
            Assert.AreEqual(profile, ControllerProfileJson.LoadFile(path));

            Assert.ThrowsExactly<ControllerProfileFormatException>(() => ControllerProfileJson.SaveFile(path, profile with { SchemaVersion = 2 }));
            Assert.AreEqual(profile, ControllerProfileJson.LoadFile(path));

            File.WriteAllBytes(path, [0xFF, 0xFE]);
            ControllerProfileFormatException encoding = Assert.ThrowsExactly<ControllerProfileFormatException>(() => ControllerProfileJson.LoadFile(path));
            Assert.AreEqual("invalid_encoding", encoding.Code);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
