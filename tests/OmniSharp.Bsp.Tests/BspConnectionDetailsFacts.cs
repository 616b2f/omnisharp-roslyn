using System.Text.Json;
using bsp_client;
using Xunit;

namespace OmniSharp.Bsp.Tests
{
    public class BspConnectionDetailsFacts
    {
        [Fact]
        public void DeserializesStandardCamelCaseBspJson()
        {
            var json = """
            {
                "name": "csharp-bsp",
                "version": "1.0.0",
                "bspVersion": "2.1.1",
                "languages": ["csharp"],
                "argv": ["dotnet", "bsp-server"]
            }
            """;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var details = JsonSerializer.Deserialize<BspConnectionDetails>(json, options);

            Assert.NotNull(details);
            Assert.Equal("csharp-bsp", details.Name);
            Assert.Equal("1.0.0", details.Version);
            Assert.Equal("2.1.1", details.BspVersion);
            Assert.Single(details.Languages);
            Assert.Contains("csharp", details.Languages);
            Assert.Equal(2, details.Argv.Length);
            Assert.Equal("dotnet", details.Argv[0]);
            Assert.Equal("bsp-server", details.Argv[1]);
        }

        [Fact]
        public void DeserializesPascalCaseBspJson()
        {
            var json = """
            {
                "Name": "test-server",
                "Version": "2.0.0",
                "BspVersion": "2.1.0",
                "Languages": ["csharp", "fsharp"],
                "Argv": ["server", "--stdio"]
            }
            """;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var details = JsonSerializer.Deserialize<BspConnectionDetails>(json, options);

            Assert.NotNull(details);
            Assert.Equal("test-server", details.Name);
            Assert.Equal("2.0.0", details.Version);
            Assert.Equal("2.1.0", details.BspVersion);
            Assert.Equal(2, details.Languages.Length);
            Assert.Contains("csharp", details.Languages);
            Assert.Contains("fsharp", details.Languages);
            Assert.Equal(new[] { "server", "--stdio" }, details.Argv);
        }
    }
}
