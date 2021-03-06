﻿namespace AWSRedrive.Interfaces
{
    public interface IMessageProcessorFactory
    {
        IMessageProcessor CreateMessageProcessor(ConfigurationEntry configuration);
    }
}
