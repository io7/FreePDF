// CI runs the net10.0 tests once more on the newest .NET runtime (DOTNET_ROLL_FORWARD=LatestMajor) and sets
// FREEPDF_EXPECT_RUNTIME_MAJOR, so the job shows which runtime ran them. MIT, see LICENSE.
using System.Globalization;
using Xunit;

namespace FreePDF.RegressionTests;

public class RuntimeTests
{
    [Fact]
    public void Tests_run_on_the_expected_runtime()
    {
        var expected = Environment.GetEnvironmentVariable("FREEPDF_EXPECT_RUNTIME_MAJOR");
        if (String.IsNullOrEmpty(expected))
            return;
        Assert.Equal(Int32.Parse(expected, CultureInfo.InvariantCulture), Environment.Version.Major);
    }
}
