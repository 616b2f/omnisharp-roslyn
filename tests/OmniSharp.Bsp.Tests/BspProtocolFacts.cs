using System;
using System.Linq;
using System.Text.Json;
using bsp4csharp.Protocol;
using Xunit;

namespace OmniSharp.Bsp.Tests
{
    public class BspProtocolFacts
    {
        [Fact]
        public void InitializeBuildParams_SerializationRoundTrip()
        {
            var initParams = new InitializeBuildParams
            {
                DisplayName = "OmniSharpTestClient",
                Version = "1.0.0",
                BspVersion = "2.1.1",
                RootUri = new Uri("file:///home/user/project"),
                Capabilities = new BuildClientCapabilities()
            };
            initParams.Capabilities.LanguageIds.Add("csharp");

            var json = JsonSerializer.Serialize(initParams);
            Assert.Contains("OmniSharpTestClient", json);
            Assert.Contains("2.1.1", json);
            Assert.Contains("csharp", json);

            var deserialized = JsonSerializer.Deserialize<InitializeBuildParams>(json);
            Assert.NotNull(deserialized);
            Assert.Equal(initParams.DisplayName, deserialized.DisplayName);
            Assert.Equal(initParams.Version, deserialized.Version);
            Assert.Equal(initParams.BspVersion, deserialized.BspVersion);
            Assert.Equal(initParams.RootUri, deserialized.RootUri);
            Assert.Contains("csharp", deserialized.Capabilities.LanguageIds);
        }

        [Fact]
        public void WorkspaceBuildTargetsResult_SerializationRoundTrip()
        {
            var result = new WorkspaceBuildTargetsResult();
            var target = new BuildTarget
            {
                Id = new BuildTargetIdentifier { Uri = new Uri("file:///home/user/project/MyApp.csproj") },
                DisplayName = "MyApp",
                BaseDirectory = new Uri("file:///home/user/project/"),
                Tags = [BuildTargetTag.Application],
                LanguageIds = ["csharp"],
                Capabilities = new BuildTargetCapabilities { CanCompile = true, CanRun = true, CanTest = false }
            };
            result.Targets = [target];

            var json = JsonSerializer.Serialize(result);
            Assert.Contains("MyApp", json);
            Assert.Contains("file:///home/user/project/MyApp.csproj", json);

            var deserialized = JsonSerializer.Deserialize<WorkspaceBuildTargetsResult>(json);
            Assert.NotNull(deserialized);
            Assert.Single(deserialized.Targets);
            var deserializedTarget = deserialized.Targets.First();
            Assert.Equal("MyApp", deserializedTarget.DisplayName);
            Assert.Equal(target.Id.Uri, deserializedTarget.Id.Uri);
            Assert.Contains("csharp", deserializedTarget.LanguageIds);
            Assert.True(deserializedTarget.Capabilities.CanCompile);
            Assert.True(deserializedTarget.Capabilities.CanRun);
            Assert.False(deserializedTarget.Capabilities.CanTest);
        }

        [Fact]
        public void SourcesResult_SerializationRoundTrip()
        {
            var result = new SourcesResult
            {
                Items =
                [
                    new SourcesItem
                    {
                        Target = new BuildTargetIdentifier { Uri = new Uri("file:///home/user/project/MyApp.csproj") },
                        Sources =
                        [
                            new SourceItem
                            {
                                Uri = new Uri("file:///home/user/project/Program.cs"),
                                Kind = SourceItemKind.File,
                                Generated = false
                            }
                        ]
                    }
                ]
            };

            var json = JsonSerializer.Serialize(result);
            Assert.Contains("Program.cs", json);

            var deserialized = JsonSerializer.Deserialize<SourcesResult>(json);
            Assert.NotNull(deserialized);
            Assert.Single(deserialized.Items);
            Assert.Single(deserialized.Items[0].Sources);
            Assert.Equal(new Uri("file:///home/user/project/Program.cs"), deserialized.Items[0].Sources[0].Uri);
            Assert.False(deserialized.Items[0].Sources[0].Generated);
        }
    }
}
