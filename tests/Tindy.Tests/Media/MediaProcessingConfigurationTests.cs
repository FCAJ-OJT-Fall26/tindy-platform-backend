using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Media;
using Xunit;

namespace Tindy.Tests.Media;

public sealed class MediaProcessingConfigurationTests
{
    [Fact]
    public void LocalConfiguration_UsesRealClamAv_AndDoesNotResolveAWS()
    {
        using var services = new ServiceCollection().AddLogging().AddMediaProcessing(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MediaLifecycle:Enabled"] = "false" }).Build()).BuildServiceProvider();
        Assert.IsType<ClamAvMalwareScanner>(services.GetRequiredService<IFileMalwareScanner>());
        Assert.Null(services.GetService<IAmazonSQS>());
        Assert.False(services.GetRequiredService<IOptions<GuardDutyOptions>>().Value.Enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GuardDutyConfiguration_ValidatesWithoutCredentialsOrAWSCalls(bool valid)
    {
        var gd = GuardDutyEventTests.Configuration();
        var settings = new Dictionary<string, string?>
        {
            ["MediaLifecycle:Enabled"] = "false", ["GuardDuty:Enabled"] = "true",
            ["GuardDuty:AccountId"] = gd.AccountId, ["GuardDuty:Region"] = gd.Region, ["GuardDuty:BucketName"] = gd.BucketName,
            ["GuardDuty:ProtectionPlanArn"] = gd.ProtectionPlanArn, ["GuardDuty:QueueUrl"] = gd.QueueUrl,
            ["MediaStorage:Provider"] = valid ? "s3" : "local", ["MediaStorage:S3:BucketName"] = gd.BucketName, ["MediaStorage:S3:Region"] = gd.Region
        };
        using var services = new ServiceCollection().AddLogging().AddMediaProcessing(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).BuildServiceProvider();
        if (valid) Assert.True(services.GetRequiredService<IOptions<GuardDutyOptions>>().Value.Enabled);
        else Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<GuardDutyOptions>>().Value);
    }
}
