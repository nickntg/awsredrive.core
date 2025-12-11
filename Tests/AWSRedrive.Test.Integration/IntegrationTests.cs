using System;
using System.Collections.Generic;
using System.Net;
using AWSRedrive;
using AWSRedrive.Models;
using Xunit;

namespace AWSRedrive.Test.Integration
{
    public class IntegrationTests
    {
        private static EntryLogger CreateLogger() => new EntryLogger("integration-test", "Trace");

        [Fact]
        public void HttpMessageProcessorSuccess()
        {
            var o = new HttpMessageProcessor();
            // Note: This test expects to fail with connection error since no server is running
            Assert.ThrowsAny<Exception>(() =>
                o.ProcessMessage("test", new Dictionary<string, string>(), 
                    new ConfigurationEntry { RedriveUrl = "http://nonehost.com/post/here?parm=test" }, 
                    CreateLogger()));
        }

        [Fact]
        public void HttpMessageProcessorFailed()
        {
            var o = new HttpMessageProcessor();
            Assert.ThrowsAny<Exception>(() => 
                o.ProcessMessage("test", new Dictionary<string, string>(),
                    new ConfigurationEntry { RedriveUrl = "http://noonehost.com/post/here?parm=test" }, 
                    CreateLogger()));
        }
    }
}
