﻿using System.Net;
using Xunit;

namespace AWSRedrive.Test.Integration
{
    public class IntegrationTests
    {
        [Fact]
        public void HttpMessageProcessorSuccess()
        {
            var o = new HttpMessageProcessor();
            o.ProcessMessage("test", new ConfigurationEntry {RedriveUrl = "http://nonehost.com/post/here?parm=test"});
            Assert.True(true);
        }

        [Fact]
        public void HttpMessageProcessorFailed()
        {
            var o = new HttpMessageProcessor();
            Assert.Throws<WebException>(() => o.ProcessMessage("test", new ConfigurationEntry { RedriveUrl = "http://noonehost.com/post/here?parm=test" }));
        }
    }
}