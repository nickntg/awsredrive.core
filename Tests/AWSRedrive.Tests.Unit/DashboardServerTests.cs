using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AWSRedrive.Models;
using AWSRedrive.Tests.Unit.Helpers;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class DashboardServerTests
    {
        private static DashboardSettings CreateSettings(int port) => new DashboardSettings
        {
            Enabled = true,
            Port = port,
            RefreshIntervalMs = 5000
        };

        [Fact]
        public void Constructor_DoesNotThrow()
        {
            var configReader = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>()
            };

            var exception = Record.Exception(() => new DashboardServer(configReader, CreateSettings(5001)));

            Assert.Null(exception);
        }

        [Fact]
        public void StartAndStop_DoesNotThrow()
        {
            var configReader = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>()
            };
            var server = new DashboardServer(configReader, CreateSettings(5002));

            var exception = Record.Exception(() =>
            {
                server.Start();
                Task.Delay(500).Wait();
                server.Stop();
            });

            Assert.Null(exception);
        }

        [Fact]
        public async Task Dashboard_ReturnsHtml()
        {
            var configReader = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>()
            };
            var server = new DashboardServer(configReader, CreateSettings(5003));
            server.Start();
            await Task.Delay(1000);

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync("http://localhost:5003/");

                Assert.True(response.IsSuccessStatusCode);
                var content = await response.Content.ReadAsStringAsync();
                Assert.Contains("AWSRedrive", content);
                Assert.Contains("<!DOCTYPE html>", content);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task ApiStatus_ReturnsJson()
        {
            var configReader = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>
                {
                    new ConfigurationEntry
                    {
                        Alias = "test-alias",
                        QueueUrl = "https://sqs.eu-west-1.amazonaws.com/123/queue",
                        RedriveUrl = "http://localhost/api",
                        Region = "eu-west-1",
                        Active = true
                    }
                }
            };
            var server = new DashboardServer(configReader, CreateSettings(5004));
            server.Start();
            await Task.Delay(1000);

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync("http://localhost:5004/api/status");

                Assert.True(response.IsSuccessStatusCode);
                var content = await response.Content.ReadAsStringAsync();
                Assert.Contains("test-alias", content);
                Assert.Contains("eu-west-1", content);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task ApiStatus_DoesNotExposeSecrets()
        {
            var configReader = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>
                {
                    new ConfigurationEntry
                    {
                        Alias = "test-alias",
                        QueueUrl = "https://sqs.eu-west-1.amazonaws.com/123/queue",
                        RedriveUrl = "http://localhost/api",
                        Region = "eu-west-1",
                        Active = true,
                        AccessKey = "AKIAIOSFODNN7EXAMPLE",
                        SecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                        AuthToken = "secret-auth-token",
                        AwsGatewayToken = "secret-gateway-token",
                        BasicAuthUserName = "user",
                        BasicAuthPassword = "secret-password"
                    }
                }
            };
            var server = new DashboardServer(configReader, CreateSettings(5005));
            server.Start();
            await Task.Delay(1000);

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync("http://localhost:5005/api/status");
                var content = await response.Content.ReadAsStringAsync();

                Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", content);
                Assert.DoesNotContain("wJalrXUtnFEMI", content);
                Assert.DoesNotContain("secret-auth-token", content);
                Assert.DoesNotContain("secret-gateway-token", content);
                Assert.DoesNotContain("secret-password", content);
                Assert.Contains("hasAccessKey", content);
                Assert.Contains("hasAuthToken", content);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public async Task ApiStatus_IncludesMetrics()
        {
            var alias = "metrics-test-alias-" + Guid.NewGuid();
            var configReader = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>
                {
                    new ConfigurationEntry
                    {
                        Alias = alias,
                        QueueUrl = "https://sqs.eu-west-1.amazonaws.com/123/queue",
                        RedriveUrl = "http://localhost/api",
                        Region = "eu-west-1",
                        Active = true
                    }
                }
            };

            var metrics = MetricsStore.GetOrCreate(alias);
            metrics.MessagesReceived = 100;
            metrics.MessagesSent = 95;
            metrics.MessagesFailed = 5;
            metrics.StartedAt = DateTime.UtcNow;

            var server = new DashboardServer(configReader, CreateSettings(5006));
            server.Start();
            await Task.Delay(1000);

            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync("http://localhost:5006/api/status");
                var content = await response.Content.ReadAsStringAsync();

                Assert.Contains("100", content);
                Assert.Contains("95", content);
                Assert.Contains("messagesReceived", content);
                Assert.Contains("messagesSent", content);
                Assert.Contains("messagesFailed", content);
            }
            finally
            {
                server.Stop();
                MetricsStore.Remove(alias);
            }
        }
    }
}
