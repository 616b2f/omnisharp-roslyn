using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using bsp_client;
using bsp4csharp.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Nerdbank.Streams;
using StreamJsonRpc;
using Xunit;

namespace OmniSharp.Bsp.Tests
{
    public class BspClientHandshakeFacts
    {
        private class MockBspServer
        {
            public bool InitializedReceived { get; private set; }
            public bool ShutdownReceived { get; private set; }

            [JsonRpcMethod(Methods.BuildInitialize, UseSingleObjectParameterDeserialization = true)]
            public InitializeBuildResult Initialize(InitializeBuildParams p)
            {
                return new InitializeBuildResult
                {
                    DisplayName = "MockBspServer",
                    Version = "1.0.0",
                    BspVersion = "2.1.1",
                    Capabilities = new BuildServerCapabilities
                    {
                        CompileProvider = new CompileProvider { LanguageIds = { "csharp" } }
                    }
                };
            }

            [JsonRpcMethod(Methods.BuildInitialized, UseSingleObjectParameterDeserialization = true)]
            public void Initialized(InitializedBuildParams p)
            {
                InitializedReceived = true;
            }

            [JsonRpcMethod(Methods.WorkspaceBuildTargets)]
            public WorkspaceBuildTargetsResult WorkspaceBuildTargets()
            {
                var result = new WorkspaceBuildTargetsResult();
                result.Targets =
                [
                    new BuildTarget
                    {
                        Id = new BuildTargetIdentifier { Uri = new Uri("file:///home/user/project/App.csproj") },
                        DisplayName = "App",
                        LanguageIds = ["csharp"],
                        Capabilities = new BuildTargetCapabilities { CanCompile = true, CanRun = true, CanTest = false }
                    }
                ];
                return result;
            }

            [JsonRpcMethod(Methods.BuildShutdown)]
            public object Shutdown()
            {
                ShutdownReceived = true;
                return new object();
            }

            [JsonRpcMethod(Methods.BuildExit)]
            public void Exit()
            {
            }
        }

        [Fact]
        public async Task ClientPerformsFullHandshakeAndReceivesTargets()
        {
            var (clientStream, serverStream) = FullDuplexStream.CreatePair();

            var server = new MockBspServer();
            var serverRpc = new JsonRpc(serverStream, serverStream, server);
            serverRpc.StartListening();

            var callbacks = new ServerCallbacks(Microsoft.Extensions.Logging.LogLevel.Debug, NullLogger<ServerCallbacks>.Instance);
            using var client = new BuildServerClient(clientStream, clientStream, callbacks);

            // 1. build/initialize
            var initParams = new InitializeBuildParams
            {
                DisplayName = "OmniSharpClient",
                Version = "1.0.0",
                BspVersion = "2.1.1",
                RootUri = new Uri("file:///home/user/project"),
                Capabilities = new BuildClientCapabilities()
            };
            var initResult = await client.BuildInitializeAsync(initParams, CancellationToken.None);

            Assert.NotNull(initResult);
            Assert.Equal("MockBspServer", initResult.DisplayName);
            Assert.Equal("2.1.1", initResult.BspVersion);

            // 2. build/initialized
            await client.BuildInitializedAsync();
            // Allow RPC dispatch
            await Task.Delay(50);
            Assert.True(server.InitializedReceived);

            // 3. workspace/buildTargets
            var targetsResult = await client.WorkspaceBuildTargetsAsync(CancellationToken.None);
            Assert.NotNull(targetsResult);
            Assert.Single(targetsResult.Targets);
            Assert.Equal("App", targetsResult.Targets.First().DisplayName);

            // 4. Server callbacks (e.g. build/logMessage and build/publishDiagnostics)
            await serverRpc.NotifyWithParameterObjectAsync(Methods.BuildLogMessage, new LogMessageParams
            {
                MessageType = MessageType.Info,
                Message = "Building project..."
            });

            await serverRpc.NotifyWithParameterObjectAsync(Methods.BuildPublishDiagnostics, new PublishDiagnosticsParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = new Uri("file:///home/user/project/Program.cs") },
                BuildTarget = new BuildTargetIdentifier { Uri = new Uri("file:///home/user/project/App.csproj") },
                Diagnostics =
                {
                    new Diagnostic
                    {
                        Message = "Test warning",
                        Severity = DiagnosticSeverity.Warning,
                        Range = new bsp4csharp.Protocol.Range
                        {
                            Start = new Position { Line = 1, Character = 1 },
                            End = new Position { Line = 1, Character = 5 }
                        }
                    }
                }
            });

            await Task.Delay(50);
            Assert.Single(callbacks.LogMessages);
            Assert.Equal("Building project...", callbacks.LogMessages.First().Message);
            Assert.Single(callbacks.Diagnostics);
            Assert.Equal("Test warning", callbacks.Diagnostics.First().Message);

            // 5. shutdown
            await client.ShutdownAsync();
            Assert.True(server.ShutdownReceived);

            await client.ExitAsync();
        }
    }
}
