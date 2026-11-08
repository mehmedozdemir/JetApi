using Avalonia;

namespace JetApi.Desktop.Tests;

public sealed class RenderingModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("software")]
    [InlineData("something-else")]
    public void Windows_RendersInSoftwareByDefault(string? preference) =>
        Program.WindowsRenderingModes(preference).ShouldBe([Win32RenderingMode.Software]);

    [Theory]
    [InlineData("gpu")]
    [InlineData("GPU")]
    public void Windows_UsesTheGpuWhenAsked(string preference) =>
        Program.WindowsRenderingModes(preference).ShouldBe([Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]);
}
