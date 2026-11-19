using System.Text.Json.Nodes;
using MineLauncher.Services.Implementations;
using Xunit;

namespace MineLauncher.Tests;

public sealed class VanillaVersionMetadataTests
{
    [Fact]
    public void ParsesClientDownloadAndAssetIndex()
    {
        var json = JsonNode.Parse("""
        {
          "id":"1.21.1","type":"release","mainClass":"net.minecraft.client.main.Main",
          "javaVersion":{"component":"21"},
          "assetIndex":{"id":"17","url":"https://example.test/index.json","sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123},
          "downloads":{"client":{"url":"https://example.test/client.jar","sha1":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","size":456}},
          "libraries":[]
        }
        """)!.AsObject();

        var version = VersionManifestService.ParseVersion(json);

        Assert.Equal("1.21.1", version.Id);
        Assert.Equal("21", version.JavaVersion.Component);
        Assert.Equal("17", version.AssetIndex.Id);
        Assert.Equal(123, version.AssetIndex.Size);
        Assert.Equal("https://example.test/client.jar", version.ClientJarUrl);
        Assert.Equal(456, version.ClientJarSize);
    }

    [Fact]
    public void ParsesArtifactAndPlatformNativeClassifier()
    {
        var json = JsonNode.Parse("""
        {
          "id":"1.20.1","mainClass":"Main",
          "libraries":[
            {
              "name":"org.example:demo:1.0",
              "downloads":{
                "artifact":{"url":"https://example.test/demo.jar","sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":12},
                "classifiers":{"natives-linux":{"url":"https://example.test/native.jar","sha1":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","size":34}}
              },
              "natives":{"linux":"natives-linux"}
            }
          ]
        }
        """)!.AsObject();

        var version = VersionManifestService.ParseVersion(json);

        Assert.Contains(version.Libraries, x => x.Classifier is null && x.Url == "https://example.test/demo.jar");
        Assert.Contains(version.Libraries, x => x.Classifier == "natives-linux" && x.Url == "https://example.test/native.jar");
    }

    [Fact]
    public void ParsesModernJvmAndGameArguments()
    {
        var json = JsonNode.Parse("""
        {
          "id":"1.21","mainClass":"Main",
          "arguments":{
            "jvm":["-XstartOnFirstThread"],
            "game":["--username",{"rules":[{"action":"allow","os":{"name":"windows"}}],"value":["--demo"]}]
          },
          "libraries":[]
        }
        """)!.AsObject();

        var version = VersionManifestService.ParseVersion(json);

        Assert.Contains(version.Arguments.Jvm, x => x.Value.Contains("-XstartOnFirstThread"));
        Assert.Contains(version.Arguments.Game, x => x.Value.Contains("--username"));
        Assert.Contains(version.Arguments.Game, x => x.Value.Contains("--demo"));
    }

    [Fact]
    public void RejectsMismatchedManifestMetadataIdAtServiceBoundary()
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            var version = VersionManifestService.ParseVersion(JsonNode.Parse("""{"id":"1.20.1","mainClass":"Main"}""")!.AsObject());
            Assert.Equal("1.20.2", version.Id);
        });
    }
}
